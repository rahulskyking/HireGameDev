namespace HireGameDev.Application.Features.Candidates;

public sealed record UploadCandidatePhotoCommand(
    Guid UserId,
    Stream Content,
    string? FileName,
    string? DeclaredContentType);

public sealed record CandidatePhotoMutationResult(
    Guid CandidateId,
    string? PhotoObjectKey,
    short Completeness,
    DateTimeOffset UpdatedAt);

public sealed record CandidateMediaViewer(
    bool IsAuthenticated,
    Guid? UserId,
    bool IsAdmin,
    bool IsStudio);
