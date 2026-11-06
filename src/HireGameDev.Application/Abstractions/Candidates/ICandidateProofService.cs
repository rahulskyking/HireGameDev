using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Candidates;

namespace HireGameDev.Application.Abstractions.Candidates;

public interface ICandidateProofService
{
    Task<CandidateOwnerProofSnapshot> GetOwnerProofForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<CandidatePublicProofSnapshot> GetPublicProofForCandidateAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default);

    Task<Result<CandidateOwnerProofSnapshot>> SaveStep4ProofAsync(
        SaveCandidateStep4ProofCommand command,
        CancellationToken cancellationToken = default);
}
