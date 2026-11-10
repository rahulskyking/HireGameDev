using System.Text;
using HireGameDev.Application.Abstractions.Media;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace HireGameDev.UnitTests.Infrastructure;

public sealed class ImageSharpImageProcessorTests
{
    private readonly ImageSharpImageProcessor _processor = new();

    [Fact]
    public async Task ProcessAsync_CandidateAvatar_CropsTo400x400WebpAndStripsExifMetadata()
    {
        using var sourceStream = new MemoryStream();
        using (var sourceImage = new Image<Rgba32>(800, 500, new Rgba32(30, 140, 220)))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Software, "SecretPhoneCamera 1.0");
            exif.SetValue(ExifTag.Copyright, "PrivateOwnerMetadata");
            sourceImage.Metadata.ExifProfile = exif;

            await sourceImage.SaveAsync(sourceStream, new JpegEncoder { Quality = 90 });
        }

        sourceStream.Position = 0;

        var result = await _processor.ProcessAsync(new ImageProcessRequest(
            SourceStream: sourceStream,
            FileName: "portrait.jpg",
            DeclaredContentType: "image/jpeg",
            Preset: ImageUploadPreset.CandidateAvatar));

        Assert.True(result.IsSuccess, result.Error.Message);
        Assert.Equal("image/webp", result.Value.ContentType);
        Assert.Equal(".webp", result.Value.Extension);
        Assert.Equal(400, result.Value.Width);
        Assert.Equal(400, result.Value.Height);
        Assert.Equal(
            DetectedImageSignature.Webp,
            ImageUploadRules.DetectSignatureFromMagicBytes(result.Value.Content));

        using var decoded = await Image.LoadAsync(new MemoryStream(result.Value.Content));
        Assert.Equal(400, decoded.Width);
        Assert.Equal(400, decoded.Height);
        Assert.Null(decoded.Metadata.ExifProfile);
        Assert.Null(decoded.Metadata.IptcProfile);
        Assert.Null(decoded.Metadata.XmpProfile);
    }

    [Fact]
    public async Task ProcessAsync_StudioLogo_PreservesAspectRatioWithin400x400Webp()
    {
        using var sourceStream = new MemoryStream();
        using (var logoImage = new Image<Rgba32>(800, 300, new Rgba32(250, 220, 40, 255)))
        {
            await logoImage.SaveAsync(sourceStream, new PngEncoder());
        }

        sourceStream.Position = 0;

        var result = await _processor.ProcessAsync(new ImageProcessRequest(
            SourceStream: sourceStream,
            FileName: "studio-logo.png",
            DeclaredContentType: "image/png",
            Preset: ImageUploadPreset.StudioLogo));

        Assert.True(result.IsSuccess, result.Error.Message);
        Assert.Equal(400, result.Value.Width);
        Assert.Equal(150, result.Value.Height);
    }

    [Fact]
    public async Task ProcessAsync_PayloadOverOneMegabyte_ReturnsTooLargeValidationError()
    {
        var oversized = new byte[ImageUploadRules.MaxImageUploadBytes + 128];
        oversized[0] = 0xFF;
        oversized[1] = 0xD8;
        oversized[2] = 0xFF;

        using var stream = new MemoryStream(oversized);
        var result = await _processor.ProcessAsync(new ImageProcessRequest(
            SourceStream: stream,
            FileName: "huge.jpg",
            DeclaredContentType: "image/jpeg",
            Preset: ImageUploadPreset.CandidateAvatar));

        Assert.True(result.IsFailure);
        Assert.Equal("Image.TooLarge", result.Error.Code);
    }

    [Fact]
    public async Task ProcessAsync_SvgSpoofedAsPng_ReturnsUnsupportedFormatError()
    {
        var svgBytes = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"400\" height=\"400\"></svg>");
        using var stream = new MemoryStream(svgBytes);

        var result = await _processor.ProcessAsync(new ImageProcessRequest(
            SourceStream: stream,
            FileName: "avatar.png",
            DeclaredContentType: "image/png",
            Preset: ImageUploadPreset.CandidateAvatar));

        Assert.True(result.IsFailure);
        Assert.Equal("Image.UnsupportedFormat", result.Error.Code);
    }

    [Fact]
    public async Task ProcessAsync_DimensionsSmallerThan32Pixels_ReturnsTooSmallError()
    {
        using var stream = new MemoryStream();
        using (var tiny = new Image<Rgba32>(16, 16, new Rgba32(10, 20, 30)))
        {
            await tiny.SaveAsync(stream, new PngEncoder());
        }

        stream.Position = 0;

        var result = await _processor.ProcessAsync(new ImageProcessRequest(
            SourceStream: stream,
            FileName: "tiny.png",
            DeclaredContentType: "image/png",
            Preset: ImageUploadPreset.CandidateAvatar));

        Assert.True(result.IsFailure);
        Assert.Equal("Image.TooSmall", result.Error.Code);
    }

    [Fact]
    public async Task ProcessAsync_DimensionsExceeding4096Pixels_ReturnsDimensionsTooLargeError()
    {
        using var stream = new MemoryStream();
        using (var wide = new Image<Rgba32>(4200, 64, new Rgba32(10, 20, 30)))
        {
            await wide.SaveAsync(stream, new PngEncoder());
        }

        stream.Position = 0;

        var result = await _processor.ProcessAsync(new ImageProcessRequest(
            SourceStream: stream,
            FileName: "decompression-bomb.png",
            DeclaredContentType: "image/png",
            Preset: ImageUploadPreset.CandidateAvatar));

        Assert.True(result.IsFailure);
        Assert.Equal("Image.DimensionsTooLarge", result.Error.Code);
    }
}
