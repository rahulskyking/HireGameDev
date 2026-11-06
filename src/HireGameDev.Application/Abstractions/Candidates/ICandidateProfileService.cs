using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Candidates;

namespace HireGameDev.Application.Abstractions.Candidates;

public interface ICandidateProfileService
{
    Task<CandidateOnboardingState> GetOnboardingStateAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<CandidateProfileSnapshot>> SaveStep1BasicsAsync(
        SaveCandidateBasicsCommand command,
        CancellationToken cancellationToken = default);

    Task<Result<CandidateProfileSnapshot>> SaveStep2ExperienceCoreAsync(
        SaveCandidateExperienceCoreCommand command,
        CancellationToken cancellationToken = default);

    Task<Result<CandidateProfileSnapshot>> SaveStep5PreferencesAsync(
        SaveCandidatePreferencesCommand command,
        CancellationToken cancellationToken = default);

    Task<Result<short>> RecalculateCompletenessAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
