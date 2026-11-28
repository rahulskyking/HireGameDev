using HireGameDev.Application.Common;

namespace HireGameDev.Application.Abstractions.Plans;

// Catalog values are proposals until connected to the entitlement resolver and enforcement.
public sealed record PlanCatalogItem(string Audience, string Plan, string Feature, string Label,
    string Value, string ValueType, IReadOnlyList<string> AllowedValues, bool AllowsUnlimited, long Version);

public interface IPlanCatalogService
{
    Task<IReadOnlyList<PlanCatalogItem>> GetAsync(CancellationToken cancellationToken = default);
    Task<Result> UpdateAsync(Guid actor, string audience, string plan, string feature, string value,
        long version, string reason, CancellationToken cancellationToken = default);
}
