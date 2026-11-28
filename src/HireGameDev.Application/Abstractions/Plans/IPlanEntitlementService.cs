using HireGameDev.Application.Common;

namespace HireGameDev.Application.Abstractions.Plans;

public sealed record PlanLimit(string Audience, string Plan, int Limit);
public sealed record PlanEntitlement(string Plan, int Limit);

// Development only. Quota-consuming transactions must hold the shared policy lock
// before taking any Studio/Candidate locks and until their transaction ends.
public interface IPlanEntitlementService
{
    Task LockForConsumptionAsync(CancellationToken cancellationToken = default);
    Task<PlanEntitlement> GetAsync(string audience, Guid profileId, CancellationToken cancellationToken = default);
    // Free-only until paid plan assignments are implemented. Read under the consumption lock for mutations.
    Task<int> GetStudioPostingLimitAsync(Guid studioId, CancellationToken cancellationToken = default);
    Task<int> GetStudioSeatLimitAsync(Guid studioId, CancellationToken cancellationToken = default);
    Task<int?> GetCandidateSavedJobLimitAsync(Guid candidateId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlanLimit>> GetLimitsAsync(CancellationToken cancellationToken = default);
    Task<Result> SetLimitAsync(Guid actor, string audience, string plan, int limit, string? reason, CancellationToken cancellationToken = default);
    Task<Result> AssignAsync(Guid actor, string audience, Guid profileId, string plan, string? reason, CancellationToken cancellationToken = default);
}
