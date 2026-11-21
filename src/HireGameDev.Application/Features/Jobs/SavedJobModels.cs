namespace HireGameDev.Application.Features.Jobs;

// Unavailable entries carry only the stable ID needed to remove the owner's save.
public sealed record SavedJobEntry(Guid JobId, DateTimeOffset SavedAt, PublicJobCard? Job);
public sealed record SavedJobPage(IReadOnlyList<SavedJobEntry> Items, int TotalCount, int Page, int PageSize, int TotalPages);
