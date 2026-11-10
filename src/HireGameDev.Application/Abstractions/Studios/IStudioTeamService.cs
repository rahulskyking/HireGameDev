using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Studios;

namespace HireGameDev.Application.Abstractions.Studios;

public interface IStudioTeamService
{
    Task<Result<StudioTeamState>> GetTeamAsync(
        Guid actorUserId,
        Guid studioId,
        CancellationToken cancellationToken = default);

    Task<Result<StudioTeamState>> AddMemberByEmailAsync(
        AddStudioMemberCommand command,
        CancellationToken cancellationToken = default);

    Task<Result<StudioTeamState>> UpdateMemberRoleAsync(
        UpdateStudioMemberRoleCommand command,
        CancellationToken cancellationToken = default);

    Task<Result<StudioTeamState>> RemoveMemberAsync(
        RemoveStudioMemberCommand command,
        CancellationToken cancellationToken = default);
}
