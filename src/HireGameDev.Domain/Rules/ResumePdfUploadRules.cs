using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Rules;

/// <summary>
/// Pure validation and classification rules for resume PDF uploads (Blueprint v2.0 Section 5 — Resume AI Import).
/// Enforces 2 MB max size, <c>.pdf</c> extension, <c>%PDF-</c> binary magic-byte header,
/// 200-character minimum extracted text (rejecting image-only/scanned PDFs), and LinkedIn PDF export detection.
/// </summary>
public static class ResumePdfUploadRules
{
    public const int MaxPdfUploadBytes = 2 * 1024 * 1024; // 2 MB (2,097,152 bytes)
    public const long MaxMultipartRequestBytes = 4L * 1024L * 1024L; // 4 MB multipart envelope guard
    public const int MinExtractedTextChars = 200;
    public const int MaxExtractedTextChars = 40_000;
    public const int MaxPdfPages = 15;
    public const int DefaultDailyParseLimit = 5;

    public static bool IsAllowedExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return true;
        }

        var trimmed = fileName.Trim();
        return trimmed.Length > 4 &&
               trimmed.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasPdfMagicHeader(ReadOnlySpan<byte> header)
    {
        // Standard PDF header signature: "%PDF-" (0x25 0x50 0x44 0x46 0x2D)
        return header.Length >= 5 &&
               header[0] == 0x25 &&
               header[1] == 0x50 &&
               header[2] == 0x44 &&
               header[3] == 0x46 &&
               header[4] == 0x2D;
    }

    public static bool ValidateExtensionAndHeader(
        string? fileName,
        ReadOnlySpan<byte> header,
        out string? errorMessage)
    {
        errorMessage = null;

        if (!string.IsNullOrWhiteSpace(fileName) && !IsAllowedExtension(fileName))
        {
            errorMessage = "Only PDF files (.pdf) are supported. For LinkedIn, use LinkedIn's 'Save to PDF' export.";
            return false;
        }

        if (!HasPdfMagicHeader(header))
        {
            errorMessage = "Uploaded file is not a valid PDF document (missing %PDF- header).";
            return false;
        }

        return true;
    }

    public static int CountNonWhitespaceChars(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                count++;
            }
        }

        return count;
    }

    public static ResumeParseSource DetectSource(
        string? extractedText,
        string? metadataCreator = null,
        string? metadataProducer = null,
        string? fileName = null)
    {
        if ((!string.IsNullOrWhiteSpace(metadataCreator) &&
             metadataCreator.Contains("LinkedIn", StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(metadataProducer) &&
             metadataProducer.Contains("LinkedIn", StringComparison.OrdinalIgnoreCase)))
        {
            return ResumeParseSource.LinkedinPdf;
        }

        if (!string.IsNullOrWhiteSpace(fileName) &&
            fileName.Trim().StartsWith("Profile", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(extractedText) &&
            extractedText.Contains("Top Skills", StringComparison.OrdinalIgnoreCase))
        {
            return ResumeParseSource.LinkedinPdf;
        }

        if (!string.IsNullOrWhiteSpace(extractedText))
        {
            var hasTopSkills = extractedText.Contains("Top Skills", StringComparison.OrdinalIgnoreCase);
            var hasPageFooter = extractedText.Contains("Page 1 of", StringComparison.OrdinalIgnoreCase);
            var hasLinkedinProfileSlug = extractedText.Contains("linkedin.com/in/", StringComparison.OrdinalIgnoreCase) &&
                                         extractedText.Contains("(LinkedIn)", StringComparison.OrdinalIgnoreCase);

            if ((hasTopSkills && hasPageFooter) || hasLinkedinProfileSlug)
            {
                return ResumeParseSource.LinkedinPdf;
            }
        }

        return ResumeParseSource.Pdf;
    }
}
