using FluentValidation;
using HireGameDev.Domain.Rules;

namespace HireGameDev.Application.Features.Candidates;

internal sealed class SaveCandidateStep4ProofCommandValidator : AbstractValidator<SaveCandidateStep4ProofCommand>
{
    public SaveCandidateStep4ProofCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.ShippedTitles)
            .NotNull()
            .Must(list => list.Count <= CandidateProfileRules.MaxShippedTitles)
            .WithMessage($"You can add at most {CandidateProfileRules.MaxShippedTitles} shipped titles.");

        RuleForEach(x => x.ShippedTitles).ChildRules(title =>
        {
            title.RuleFor(t => t.GameName)
                .Must(v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length is >= 1 and <= 160)
                .WithMessage("Each shipped title requires an internal project/game name (up to 160 characters).");

            title.RuleFor(t => t.StudioName)
                .Must(v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length is >= 1 and <= 160)
                .WithMessage("Each shipped title requires a studio or team name (up to 160 characters).");

            title.RuleFor(t => t.Role)
                .Must(v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length is >= 1 and <= 120)
                .WithMessage("Each shipped title requires your credited role (up to 120 characters).");

            title.RuleFor(t => t.Platforms)
                .NotNull()
                .Must(p => p.Count <= CandidateProfileRules.AllowedGamePlatforms.Count && p.All(CandidateProfileRules.IsValidGamePlatform))
                .WithMessage("Select valid target platforms (pc, android, ios, console, web).");

            title.RuleFor(t => t.EngineSkillId)
                .Must(id => !id.HasValue || id.Value > 0)
                .WithMessage("Selected game engine skill is invalid.");

            title.RuleFor(t => t.ReleaseYear)
                .Must(y => !y.HasValue || (y.Value >= CandidateProfileRules.MinShippedTitleReleaseYear && y.Value <= CandidateProfileRules.MaxShippedTitleReleaseYear))
                .WithMessage($"Release year must be between {CandidateProfileRules.MinShippedTitleReleaseYear} and {CandidateProfileRules.MaxShippedTitleReleaseYear}.");

            title.RuleFor(t => t.StoreUrl)
                .Custom((storeUrl, ctx) =>
                {
                    if (string.IsNullOrWhiteSpace(storeUrl))
                    {
                        return;
                    }

                    if (!ExternalUrlRules.TryNormalizeHttpsUrl(storeUrl, out _, out var error))
                    {
                        ctx.AddFailure($"Shipped title store URL invalid: {error}");
                    }
                });

            title.RuleFor(t => t)
                .Must(t => !t.HideStudioInPublic || t.IsNda)
                .WithMessage("Hiding the studio name publicly requires enabling 'Under NDA' on that credit.");
        });

        RuleFor(x => x.PortfolioLinks)
            .NotNull()
            .Must(list => list.Count <= CandidateProfileRules.MaxPortfolioLinks)
            .WithMessage($"You can add at most {CandidateProfileRules.MaxPortfolioLinks} portfolio links.");

        RuleForEach(x => x.PortfolioLinks).ChildRules(link =>
        {
            link.RuleFor(l => l.LinkType)
                .IsInEnum()
                .WithMessage("Select a valid portfolio link type.");

            link.RuleFor(l => l.Url)
                .Custom((url, ctx) =>
                {
                    if (!ExternalUrlRules.TryNormalizeHttpsUrl(url, out _, out var error))
                    {
                        ctx.AddFailure($"Portfolio link URL invalid: {error}");
                    }
                });

            link.RuleFor(l => l.Title)
                .Must(t => string.IsNullOrEmpty(t) || t.Trim().Length <= 160)
                .WithMessage("Portfolio link title must be 160 characters or fewer.");

            link.RuleFor(l => l.SkillIds)
                .NotNull()
                .Must(ids => ids.Count <= CandidateProfileRules.MaxEvidencedSkillsPerLink && ids.All(id => id > 0))
                .WithMessage($"Each portfolio link can tag at most {CandidateProfileRules.MaxEvidencedSkillsPerLink} valid skills.");
        });
    }
}
