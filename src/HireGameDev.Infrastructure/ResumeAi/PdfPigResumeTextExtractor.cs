using System.Text;
using HireGameDev.Application.Abstractions.Candidates;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Candidates;
using HireGameDev.Domain.Rules;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace HireGameDev.Infrastructure.ResumeAi;

/// <summary>
/// Bounded, in-memory PDF text extractor backed by <c>PdfPig</c>.
/// Validates <c>.pdf</c> extension, 2 MB byte ceiling, <c>%PDF-</c> magic-byte signature, and page-count bounds,
/// without ever writing uploaded PDFs to disk.
/// </summary>
internal sealed class PdfPigResumeTextExtractor : IResumePdfTextExtractor
{
    public async Task<Result<ExtractedResumePdfDocument>> ExtractTextAsync(
        Stream pdfStream,
        string? fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);

        if (pdfStream.CanSeek && pdfStream.Length == 0)
        {
            return Result.Failure<ExtractedResumePdfDocument>(
                Error.Validation("ResumeImport.EmptyFile", "Please choose a non-empty PDF file to import."));
        }

        if (pdfStream.CanSeek && pdfStream.Length > ResumePdfUploadRules.MaxPdfUploadBytes)
        {
            return Result.Failure<ExtractedResumePdfDocument>(
                Error.Validation("ResumeImport.FileTooLarge", "Resume PDF must be 2 MB or smaller."));
        }

        var readResult = await ReadBoundedBytesAsync(pdfStream, ResumePdfUploadRules.MaxPdfUploadBytes, cancellationToken);
        if (readResult.IsFailure)
        {
            return Result.Failure<ExtractedResumePdfDocument>(readResult.Error);
        }

        var pdfBytes = readResult.Value;
        try
        {
            if (!ResumePdfUploadRules.ValidateExtensionAndHeader(fileName, pdfBytes, out var headerError))
            {
                return Result.Failure<ExtractedResumePdfDocument>(
                    Error.Validation("ResumeImport.InvalidPdfHeader", headerError!));
            }

            using var document = PdfDocument.Open(pdfBytes);

            if (document.IsEncrypted)
            {
                return Result.Failure<ExtractedResumePdfDocument>(
                    Error.Validation(
                        "ResumeImport.EncryptedPdf",
                        "Password-protected PDFs cannot be imported. Please export an unprotected PDF and try again."));
            }

            var pageCount = document.NumberOfPages;
            if (pageCount <= 0)
            {
                return Result.Failure<ExtractedResumePdfDocument>(
                    Error.Validation("ResumeImport.EmptyPdfPages", "The uploaded PDF has no readable pages."));
            }

            if (pageCount > ResumePdfUploadRules.MaxPdfPages)
            {
                return Result.Failure<ExtractedResumePdfDocument>(
                    Error.Validation(
                        "ResumeImport.TooManyPages",
                        $"Resume PDF cannot exceed {ResumePdfUploadRules.MaxPdfPages} pages (uploaded PDF has {pageCount} pages)."));
            }

            var sb = new StringBuilder(Math.Min(pdfBytes.Length, 16_384));

            for (var p = 1; p <= pageCount; p++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = document.GetPage(p);
                var pageText = ContentOrderTextExtractor.GetText(page, addDoubleNewline: true);

                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    if (sb.Length > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine();
                    }

                    sb.Append(pageText.Trim());
                }

                if (sb.Length >= ResumePdfUploadRules.MaxExtractedTextChars)
                {
                    break;
                }
            }

            var fullText = sb.Length > ResumePdfUploadRules.MaxExtractedTextChars
                ? sb.ToString(0, ResumePdfUploadRules.MaxExtractedTextChars)
                : sb.ToString();

            var nonWhitespaceChars = ResumePdfUploadRules.CountNonWhitespaceChars(fullText);
            var detectedSource = ResumePdfUploadRules.DetectSource(
                extractedText: fullText,
                metadataCreator: document.Information?.Creator,
                metadataProducer: document.Information?.Producer,
                fileName: fileName);

            return Result.Success(new ExtractedResumePdfDocument(
                ExtractedText: fullText,
                NonWhitespaceCharCount: nonWhitespaceChars,
                PageCount: pageCount,
                DetectedSource: detectedSource));
        }
        catch (PdfDocumentEncryptedException)
        {
            return Result.Failure<ExtractedResumePdfDocument>(
                Error.Validation(
                    "ResumeImport.EncryptedPdf",
                    "Password-protected PDFs cannot be imported. Please export an unprotected PDF and try again."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Result.Failure<ExtractedResumePdfDocument>(
                Error.Validation(
                    "ResumeImport.CorruptPdf",
                    "The uploaded file could not be parsed as a valid PDF document."));
        }
        finally
        {
            // Zero out raw PDF bytes in memory as soon as extraction finishes.
            Array.Clear(pdfBytes, 0, pdfBytes.Length);
        }
    }

    private static async Task<Result<byte[]>> ReadBoundedBytesAsync(
        Stream stream,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read <= 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                Array.Clear(buffer, 0, buffer.Length);
                return Result.Failure<byte[]>(
                    Error.Validation("ResumeImport.FileTooLarge", "Resume PDF must be 2 MB or smaller."));
            }

            ms.Write(buffer, 0, read);
        }

        Array.Clear(buffer, 0, buffer.Length);

        if (total == 0)
        {
            return Result.Failure<byte[]>(
                Error.Validation("ResumeImport.EmptyFile", "Please choose a non-empty PDF file to import."));
        }

        return Result.Success(ms.ToArray());
    }
}
