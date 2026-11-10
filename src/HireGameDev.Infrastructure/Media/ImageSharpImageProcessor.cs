using HireGameDev.Application.Abstractions.Media;
using HireGameDev.Application.Common;
using HireGameDev.Domain.Rules;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace HireGameDev.Infrastructure.Media;

/// <summary>
/// SixLabors.ImageSharp implementation of <see cref="IImageProcessor"/> (ADR-016).
/// Performs bounded stream reading (1 MB max), extension + binary magic-byte validation,
/// header-only dimension guarding (&lt;= 4096x4096), EXIF orientation normalization,
/// privacy metadata stripping (EXIF/IPTC/XMP/CICP), resizing, and WebP encoding in memory.
/// </summary>
public sealed class ImageSharpImageProcessor : IImageProcessor
{
    public async Task<Result<ProcessedImagePayload>> ProcessAsync(
        ImageProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SourceStream);

        var readOutcome = await ReadBoundedBytesAsync(
            request.SourceStream,
            ImageUploadRules.MaxImageUploadBytes,
            cancellationToken);

        if (readOutcome.ExceededLimit)
        {
            return Result.Failure<ProcessedImagePayload>(
                Error.Validation("Image.TooLarge", "Image file must be 1 MB or smaller."));
        }

        if (readOutcome.Bytes.Length == 0)
        {
            return Result.Failure<ProcessedImagePayload>(
                Error.Validation("Image.Empty", "Select a non-empty image file to upload."));
        }

        var headerLength = Math.Min(readOutcome.Bytes.Length, 16);
        if (!ImageUploadRules.ValidateExtensionAndSignature(
            request.FileName,
            readOutcome.Bytes.AsSpan(0, headerLength),
            out _,
            out var signatureError))
        {
            return Result.Failure<ProcessedImagePayload>(
                Error.Validation("Image.UnsupportedFormat", signatureError!));
        }

        using var sourceMemory = new MemoryStream(readOutcome.Bytes, writable: false);

        ImageInfo imageInfo;
        try
        {
            imageInfo = await Image.IdentifyAsync(sourceMemory, cancellationToken);
        }
        catch (Exception ex) when (
            ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            return Result.Failure<ProcessedImagePayload>(
                Error.Validation(
                    "Image.CorruptOrUnsupported",
                    "The uploaded image could not be decoded. Upload a valid JPG, PNG, or WebP file."));
        }

        if (imageInfo.Width < ImageUploadRules.MinPixelDimension ||
            imageInfo.Height < ImageUploadRules.MinPixelDimension)
        {
            return Result.Failure<ProcessedImagePayload>(
                Error.Validation(
                    "Image.TooSmall",
                    $"Image must be at least {ImageUploadRules.MinPixelDimension}x{ImageUploadRules.MinPixelDimension} pixels."));
        }

        if (imageInfo.Width > ImageUploadRules.MaxPixelDimension ||
            imageInfo.Height > ImageUploadRules.MaxPixelDimension)
        {
            return Result.Failure<ProcessedImagePayload>(
                Error.Validation(
                    "Image.DimensionsTooLarge",
                    $"Image dimensions cannot exceed {ImageUploadRules.MaxPixelDimension}x{ImageUploadRules.MaxPixelDimension} pixels."));
        }

        sourceMemory.Position = 0;

        try
        {
            using var image = await Image.LoadAsync(sourceMemory, cancellationToken);

            var targetDimension = request.Preset == ImageUploadPreset.StudioLogo
                ? ImageUploadRules.StudioLogoSizePx
                : ImageUploadRules.CandidateAvatarSizePx;

            var resizeMode = request.Preset == ImageUploadPreset.StudioLogo
                ? ResizeMode.Max
                : ResizeMode.Crop;

            // Apply EXIF orientation before stripping metadata profiles so smartphone portraits stay upright.
            image.Mutate(ctx => ctx
                .AutoOrient()
                .Resize(new ResizeOptions
                {
                    Size = new Size(targetDimension, targetDimension),
                    Mode = resizeMode
                }));

            image.Metadata.ExifProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.CicpProfile = null;

            using var outputMemory = new MemoryStream();
            var encoder = new WebpEncoder
            {
                FileFormat = WebpFileFormatType.Lossy,
                Quality = request.Preset == ImageUploadPreset.StudioLogo ? 85 : 82
            };

            await image.SaveAsync(outputMemory, encoder, cancellationToken);

            return Result.Success(new ProcessedImagePayload(
                Content: outputMemory.ToArray(),
                ContentType: "image/webp",
                Extension: ".webp",
                Width: image.Width,
                Height: image.Height));
        }
        catch (Exception ex) when (
            ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            return Result.Failure<ProcessedImagePayload>(
                Error.Validation(
                    "Image.CorruptOrUnsupported",
                    "The uploaded image could not be processed. Upload a valid JPG, PNG, or WebP file."));
        }
    }

    private static async Task<(byte[] Bytes, bool ExceededLimit)> ReadBoundedBytesAsync(
        Stream stream,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            var remaining = stream.Length - stream.Position;
            if (remaining > maxBytes)
            {
                return ([], true);
            }
        }

        using var accumulator = new MemoryStream();
        var chunk = new byte[16_384];
        var totalRead = 0;

        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
            if (totalRead > maxBytes)
            {
                return ([], true);
            }

            accumulator.Write(chunk, 0, read);
        }

        return (accumulator.ToArray(), false);
    }
}
