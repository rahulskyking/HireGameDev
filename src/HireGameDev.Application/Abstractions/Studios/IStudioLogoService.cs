using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Studios;

namespace HireGameDev.Application.Abstractions.Studios;

public interface IStudioLogoService
{
    Task<Result<StudioLogoMutationResult>> UploadLogoAsync(
        UploadStudioLogoCommand command,
        CancellationToken cancellationToken = default);

    Task<Result<StudioLogoMutationResult>> RemoveLogoAsync(
        Guid actorUserId,
        Guid studioId,
        CancellationToken cancellationToken = default);
}
