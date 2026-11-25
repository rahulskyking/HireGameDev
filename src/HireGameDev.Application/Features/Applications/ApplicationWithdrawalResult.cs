namespace HireGameDev.Application.Features.Applications;

/// <summary>A retry of an already-withdrawn application succeeds without a second change.</summary>
public sealed record ApplicationWithdrawalResult(bool Applied);
