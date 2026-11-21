namespace HireGameDev.Application.Features.Applications;

public sealed record ApplicationReviewOffer(ApplicationSnapshotDraft Snapshot, string ReviewToken, string ConsentVersion, string ConsentText, DateTimeOffset ExpiresAt);
public sealed record SubmitApplicationCommand(Guid ActorUserId, Guid JobId, string? ScreeningAnswer, string ReviewToken, bool Consent);
public sealed record SubmittedApplication(Guid ApplicationId, DateTimeOffset SubmittedAt);
