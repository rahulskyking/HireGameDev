using System.Text;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using Xunit;

namespace HireGameDev.UnitTests.Domain;

public sealed class ResumePiiRedactorAndPdfRulesTests
{
    [Fact]
    public void ValidateExtensionAndHeader_AcceptsValidPdfMagicBytes_AndRejectsSpoofedFiles()
    {
        var validHeader = Encoding.ASCII.GetBytes("%PDF-1.7\n");
        Assert.True(ResumePdfUploadRules.ValidateExtensionAndHeader("rahul-resume.pdf", validHeader, out var okErr));
        Assert.Null(okErr);

        Assert.False(ResumePdfUploadRules.ValidateExtensionAndHeader("rahul-resume.docx", validHeader, out var extErr));
        Assert.Contains(".pdf", extErr, StringComparison.OrdinalIgnoreCase);

        var htmlBytes = Encoding.ASCII.GetBytes("<html><body>Not a PDF</body></html>");
        Assert.False(ResumePdfUploadRules.ValidateExtensionAndHeader("spoofed.pdf", htmlBytes, out var magicErr));
        Assert.Contains("%PDF-", magicErr, StringComparison.Ordinal);
    }

    [Fact]
    public void DetectSource_DistinguishesLinkedinPdfExportFromStandardResumePdf()
    {
        var linkedinText = "Contact\nTop Skills\nUnity\nC#\nPage 1 of 2";
        Assert.Equal(
            ResumeParseSource.LinkedinPdf,
            ResumePdfUploadRules.DetectSource(linkedinText, metadataCreator: null, metadataProducer: null, fileName: "Profile.pdf"));

        Assert.Equal(
            ResumeParseSource.LinkedinPdf,
            ResumePdfUploadRules.DetectSource("Senior Gameplay Programmer", metadataCreator: "LinkedIn", metadataProducer: null));

        Assert.Equal(
            ResumeParseSource.Pdf,
            ResumePdfUploadRules.DetectSource("Senior Gameplay Programmer\nExperience\nUnity Developer", metadataCreator: "LaTeX"));
    }

    [Fact]
    public void RedactWithReport_StripsEmailsUrlsAndIndianOrInternationalPhones_WhilePreservingTechTokensAndDates()
    {
        const string sampleResume =
            """
            Rahul Verma
            Senior Gameplay Programmer | 4.5 years of experience
            Email: rahul.verma+jobs@gamedev.co.in | Phone: +91 98765 43210 | Alt: 098111-22334 | US: +1 (415) 555-0199
            Links: https://rahulverma.dev/reel, linkedin.com/in/rahul-verma-dev, github.com/rahulverma, rahul.itch.io
            Skills: C++, C#, Unity, Unreal Engine, Node.js, Three.js, ASP.NET
            Experience:
            Gameplay Programmer — Nodding Heads Games (2020-06 - 2024-08)
            Education:
            B.Tech in Computer Science — IIT Guwahati (2016 - 2020)
            Expected CTC: 18.5 LPA
            """;

        var report = ResumePiiRedactor.RedactWithReport(sampleResume);

        Assert.Equal(1, report.EmailsRedacted);
        Assert.True(report.UrlsRedacted >= 4);
        Assert.Equal(3, report.PhonesRedacted);

        // Sensitive contact strings must be completely absent from redacted output.
        Assert.DoesNotContain("rahul.verma+jobs@gamedev.co.in", report.RedactedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("98765 43210", report.RedactedText, StringComparison.Ordinal);
        Assert.DoesNotContain("98111-22334", report.RedactedText, StringComparison.Ordinal);
        Assert.DoesNotContain("555-0199", report.RedactedText, StringComparison.Ordinal);
        Assert.DoesNotContain("rahulverma.dev", report.RedactedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("linkedin.com/in/rahul-verma-dev", report.RedactedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("github.com/rahulverma", report.RedactedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rahul.itch.io", report.RedactedText, StringComparison.OrdinalIgnoreCase);

        // Technical stack tokens and employment/education timelines must remain completely intact!
        Assert.Contains("C++", report.RedactedText, StringComparison.Ordinal);
        Assert.Contains("C#", report.RedactedText, StringComparison.Ordinal);
        Assert.Contains("Node.js", report.RedactedText, StringComparison.Ordinal);
        Assert.Contains("Three.js", report.RedactedText, StringComparison.Ordinal);
        Assert.Contains("ASP.NET", report.RedactedText, StringComparison.Ordinal);
        Assert.Contains("2020-06 - 2024-08", report.RedactedText, StringComparison.Ordinal);
        Assert.Contains("2016 - 2020", report.RedactedText, StringComparison.Ordinal);
        Assert.Contains("4.5 years of experience", report.RedactedText, StringComparison.Ordinal);
        Assert.Contains("18.5 LPA", report.RedactedText, StringComparison.Ordinal);
    }
}
