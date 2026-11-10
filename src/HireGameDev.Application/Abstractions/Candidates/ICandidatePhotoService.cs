using HireGameDev.Application.Abstractions.Storage;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Candidates;

namespace HireGameDev.Application.Abstractions.Candidates;

/// <summary>
/// Manages candidate avatar upload, replacement, removal, completeness recalculation (+5%),
/// and authorized read access without any coupling to local filesystem paths (ADR-016).
/// </summary>
public interface ICandidatePhotoService
{
    Task<Result<CandidatePhotoMutationResult>> UploadPhotoAsync(
        UploadCandidatePhotoCommand command,
        CancellationToken cancellationToken = default);

    Task<Result<CandidatePhotoMutationResult>> RemovePhotoAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<StoredObjectReadResult>> OpenAuthorizedAvatarAsync(
        Guid candidateId,
        CandidateMediaViewer viewer,
        CancellationToken cancellationToken = default);
}
