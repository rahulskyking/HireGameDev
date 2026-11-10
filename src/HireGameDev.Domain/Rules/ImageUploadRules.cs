namespace HireGameDev.Domain.Rules;

public enum DetectedImageSignature
{
    Unknown = 0,
    Jpeg = 1,
    Png = 2,
    Webp = 3
}

/// <summary>
/// Pure domain rules for uploaded candidate avatars and studio logos (Blueprint v2.0 Sections 5, 6 &amp; 13.A).
/// Enforces 1 MB max payload, explicit raster extension allowlist (rejecting SVG/GIF/executables),
/// and binary magic-byte header verification independent of declared MIME headers.
/// </summary>
public static class ImageUploadRules
{
    public const int MaxImageUploadBytes = 1_048_576; // 1 MB
    public const long MaxMultipartRequestBytes = 5L * 1024L * 1024L; // 5 MB form envelope guard
    public const int MinPixelDimension = 32;
    public const int MaxPixelDimension = 4096;
    public const int CandidateAvatarSizePx = 400;
    public const int StudioLogoSizePx = 400;

    public static readonly IReadOnlyList<string> AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public static bool IsAllowedExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return true;
        }

        var trimmed = fileName.Trim();
        var dotIndex = trimmed.LastIndexOf('.');
        if (dotIndex <= 0 || dotIndex == trimmed.Length - 1)
        {
            return false;
        }

        var ext = trimmed[dotIndex..].ToLowerInvariant();
        for (var i = 0; i < AllowedExtensions.Count; i++)
        {
            if (AllowedExtensions[i] == ext)
            {
                return true;
            }
        }

        return false;
    }

    public static DetectedImageSignature DetectSignatureFromMagicBytes(ReadOnlySpan<byte> header)
    {
        // JPEG: FF D8 FF
        if (header.Length >= 3 &&
            header[0] == 0xFF &&
            header[1] == 0xD8 &&
            header[2] == 0xFF)
        {
            return DetectedImageSignature.Jpeg;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (header.Length >= 8 &&
            header[0] == 0x89 &&
            header[1] == 0x50 &&
            header[2] == 0x4E &&
            header[3] == 0x47 &&
            header[4] == 0x0D &&
            header[5] == 0x0A &&
            header[6] == 0x1A &&
            header[7] == 0x0A)
        {
            return DetectedImageSignature.Png;
        }

        // WebP: "RIFF" (52 49 46 46) at [0..4] and "WEBP" (57 45 42 50) at [8..12]
        if (header.Length >= 12 &&
            header[0] == 0x52 &&
            header[1] == 0x49 &&
            header[2] == 0x46 &&
            header[3] == 0x46 &&
            header[8] == 0x57 &&
            header[9] == 0x45 &&
            header[10] == 0x42 &&
            header[11] == 0x50)
        {
            return DetectedImageSignature.Webp;
        }

        return DetectedImageSignature.Unknown;
    }

    public static bool ValidateExtensionAndSignature(
        string? fileName,
        ReadOnlySpan<byte> header,
        out DetectedImageSignature signature,
        out string? errorMessage)
    {
        signature = DetectedImageSignature.Unknown;
        errorMessage = null;

        if (!string.IsNullOrWhiteSpace(fileName) && !IsAllowedExtension(fileName))
        {
            errorMessage = "Only .jpg, .jpeg, .png, and .webp image files are allowed (SVG and GIF are not supported).";
            return false;
        }

        signature = DetectSignatureFromMagicBytes(header);
        if (signature == DetectedImageSignature.Unknown)
        {
            errorMessage = "Uploaded file is not a valid JPEG, PNG, or WebP image.";
            return false;
        }

        return true;
    }
}
