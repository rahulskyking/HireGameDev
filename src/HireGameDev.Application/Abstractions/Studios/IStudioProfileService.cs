using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Studios;

namespace HireGameDev.Application.Abstractions.Studios;

public interface IStudioProfileService
{
    Task<IReadOnlyList<StudioMembershipSummary>> GetUserMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<StudioMembershipSummary?> GetOwnedActiveStudioSummaryAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<StudioProfileDetail>> GetStudioForMemberAsync(
        Guid actorUserId,
        Guid? requestedStudioId = null,
        CancellationToken cancellationToken = default);

    Task<Result<StudioSlugAvailabilityResult>> CheckSlugAvailabilityAsync(
        string rawSlug,
        Guid? excludeStudioId = null,
        CancellationToken cancellationToken = default);

    Task<Result<StudioProfileDetail>> CreateStudioAsync(
        CreateStudioProfileCommand command,
        CancellationToken cancellationToken = default);

    Task<Result<StudioProfileDetail>> UpdateStudioProfileAsync(
        UpdateStudioProfileCommand command,
        CancellationToken cancellationToken = default);

    Task<Result<PublicStudioPageModel>> GetPublicStudioBySlugAsync(
        string rawSlug,
        PublicStudioViewer viewer,
        CancellationToken cancellationToken = default);
}
