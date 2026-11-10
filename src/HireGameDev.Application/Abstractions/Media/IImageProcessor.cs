using HireGameDev.Application.Common;

namespace HireGameDev.Application.Abstractions.Media;

public enum ImageUploadPreset
{
    CandidateAvatar = 0,
    StudioLogo = 1
}

public sealed record ImageProcessRequest(
    Stream SourceStream,
    string? FileName,
    string? DeclaredContentType,
    ImageUploadPreset Preset);

public sealed record ProcessedImagePayload(
    byte[] Content,
    string ContentType,
    string Extension,
    int Width,
    int Height);

/// <summary>
/// Decoupled in-memory image validation, orientation normalization, metadata stripping,
/// resizing, and WebP encoding pipeline (ADR-016). Contains zero storage or filesystem logic.
/// </summary>
public interface IImageProcessor
{
    Task<Result<ProcessedImagePayload>> ProcessAsync(
        ImageProcessRequest request,
        CancellationToken cancellationToken = default);
}
