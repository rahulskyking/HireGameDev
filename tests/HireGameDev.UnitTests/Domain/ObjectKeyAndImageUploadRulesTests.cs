using System.Text;
using HireGameDev.Domain.Rules;
using Xunit;

namespace HireGameDev.UnitTests.Domain;

public sealed class ObjectKeyAndImageUploadRulesTests
{
    [Fact]
    public void BuildRandomWebpKey_ProducesNormalizedRelativeObjectKey()
    {
        var avatarKey = ObjectKeyRules.BuildRandomWebpKey(ObjectKeyRules.CandidateAvatarsPrefix);
        var logoKey = ObjectKeyRules.BuildRandomWebpKey(ObjectKeyRules.StudioLogosPrefix);

        Assert.StartsWith("avatars/", avatarKey, StringComparison.Ordinal);
        Assert.EndsWith(".webp", avatarKey, StringComparison.Ordinal);
        Assert.StartsWith("logos/", logoKey, StringComparison.Ordinal);
        Assert.EndsWith(".webp", logoKey, StringComparison.Ordinal);

        Assert.True(ObjectKeyRules.TryNormalize(avatarKey, out var normalized, out _));
        Assert.Equal(avatarKey, normalized);
    }

    [Theory]
    [InlineData("avatars/0123456789abcdef0123456789abcdef.webp", "avatars/0123456789abcdef0123456789abcdef.webp")]
    [InlineData("Avatars/ABC_123-def.WEBP", "avatars/abc_123-def.webp")]
    [InlineData("logos/studio-01.webp", "logos/studio-01.webp")]
    public void TryNormalize_ValidRelativeObjectKeys_Succeed(string rawKey, string expected)
    {
        var ok = ObjectKeyRules.TryNormalize(rawKey, out var normalized, out var error);

        Assert.True(ok, error);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("orphan.webp")]
    [InlineData("/avatars/file.webp")]
    [InlineData("avatars/file.webp/")]
    [InlineData("../avatars/file.webp")]
    [InlineData("avatars/../file.webp")]
    [InlineData("avatars/./file.webp")]
    [InlineData("avatars/.hidden.webp")]
    [InlineData("avatars\\file.webp")]
    [InlineData("C:/uploads/avatars/file.webp")]
    [InlineData("https://cdn.example.com/avatars/file.webp")]
    [InlineData("avatars/file.webp?token=123")]
    [InlineData("avatars/file name.webp")]
    public void TryNormalize_UnsafeOrNonRelativeKeys_AreRejected(string unsafeKey)
    {
        var ok = ObjectKeyRules.TryNormalize(unsafeKey, out var normalized, out var error);

        Assert.False(ok);
        Assert.Empty(normalized);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void ValidateExtensionAndSignature_AcceptsValidJpegPngAndWebpHeaders()
    {
        byte[] jpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
        byte[] pngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
        byte[] webpHeader =
        [
            0x52, 0x49, 0x46, 0x46,
            0x24, 0x00, 0x00, 0x00,
            0x57, 0x45, 0x42, 0x50,
            0x56, 0x50, 0x38, 0x20
        ];

        Assert.True(ImageUploadRules.ValidateExtensionAndSignature("headshot.jpg", jpegHeader, out var jpegSig, out _));
        Assert.Equal(DetectedImageSignature.Jpeg, jpegSig);

        Assert.True(ImageUploadRules.ValidateExtensionAndSignature("headshot.jpeg", jpegHeader, out _, out _));

        Assert.True(ImageUploadRules.ValidateExtensionAndSignature("avatar.png", pngHeader, out var pngSig, out _));
        Assert.Equal(DetectedImageSignature.Png, pngSig);

        Assert.True(ImageUploadRules.ValidateExtensionAndSignature("logo.webp", webpHeader, out var webpSig, out _));
        Assert.Equal(DetectedImageSignature.Webp, webpSig);
    }

    [Theory]
    [InlineData("vector.svg")]
    [InlineData("animated.gif")]
    [InlineData("malware.exe")]
    [InlineData("resume.pdf")]
    [InlineData("double.svg.exe")]
    public void ValidateExtensionAndSignature_DisallowedExtensions_AreRejectedEvenWithPngBytes(string fileName)
    {
        byte[] pngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        var ok = ImageUploadRules.ValidateExtensionAndSignature(fileName, pngHeader, out _, out var error);

        Assert.False(ok);
        Assert.Contains("not supported", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateExtensionAndSignature_SvgGifOrPdfDisguisedAsPng_AreRejectedByMagicBytes()
    {
        var svgBytes = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");
        var gifBytes = Encoding.ASCII.GetBytes("GIF89a\x01\x00\x01\x00");
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\n");

        Assert.False(ImageUploadRules.ValidateExtensionAndSignature("spoof.png", svgBytes, out _, out _));
        Assert.False(ImageUploadRules.ValidateExtensionAndSignature("spoof.jpg", gifBytes, out _, out _));
        Assert.False(ImageUploadRules.ValidateExtensionAndSignature("spoof.webp", pdfBytes, out _, out _));
    }
}
