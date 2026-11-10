using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Candidates;

namespace HireGameDev.Application.Abstractions.Candidates;

public interface ICandidateResumeImportService
{
    Task<ResumeParseQuotaStatus> GetQuotaStatusAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<ResumeImportDraft>> ParseResumePdfAsync(
        ParseCandidateResumeCommand command,
        CancellationToken cancellationToken = default);

    Task<Result<ApplyCandidateResumeResult>> ApplyImportedDraftAsync(
        ApplyCandidateResumeDraftCommand command,
        CancellationToken cancellationToken = default);
}
