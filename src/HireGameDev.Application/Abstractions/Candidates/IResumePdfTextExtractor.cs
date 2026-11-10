using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Candidates;

namespace HireGameDev.Application.Abstractions.Candidates;

public interface IResumePdfTextExtractor
{
    Task<Result<ExtractedResumePdfDocument>> ExtractTextAsync(
        Stream pdfStream,
        string? fileName,
        CancellationToken cancellationToken = default);
}
