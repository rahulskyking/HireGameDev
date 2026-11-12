using FluentValidation;
using HireGameDev.Domain.Rules;

namespace HireGameDev.Application.Features.Candidates;

internal sealed class UpdateCandidatePrivacySettingsCommandValidator
    : AbstractValidator<UpdateCandidatePrivacySettingsCommand>
{
    public UpdateCandidatePrivacySettingsCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.Availability)
            .IsInEnum().WithMessage("Select a valid availability status.");

        RuleFor(x => x.CurrentCtcLpa)
            .Must(v => v is null || (v.Value >= 0m && v.Value <= CandidateProfileRules.MaxCandidateCtcLpa))
            .WithMessage($"Current CTC (₹ LPA) must be between 0 and {CandidateProfileRules.MaxCandidateCtcLpa:0} LPA.");
    }
}

internal sealed class BlockStudioCommandValidator : AbstractValidator<BlockStudioCommand>
{
    public BlockStudioCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.StudioId)
            .NotEmpty().WithMessage("Select a verified studio to block.");
    }
}

internal sealed class UnblockStudioCommandValidator : AbstractValidator<UnblockStudioCommand>
{
    public UnblockStudioCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.StudioId)
            .NotEmpty().WithMessage("Select a blocked studio to remove.");
    }
}
