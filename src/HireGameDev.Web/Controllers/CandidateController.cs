using System.Security.Claims;
using HireGameDev.Application.Abstractions.Candidates;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Features.Candidates;
using HireGameDev.Application.Features.ReferenceData;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Web.Authorization;
using HireGameDev.Web.Models.Candidate;
using HireGameDev.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.CandidateOnly)]
[Route("Candidate")]
public sealed class CandidateController : Controller
{
    private readonly ICandidateProfileService _profileService;
    private readonly ICandidateSkillsService _skillsService;
    private readonly IReferenceDataService _referenceDataService;
    private readonly IPortalNavigator _portalNavigator;

    public CandidateController(
        ICandidateProfileService profileService,
        ICandidateSkillsService skillsService,
        IReferenceDataService referenceDataService,
        IPortalNavigator portalNavigator)
    {
        _profileService = profileService;
        _skillsService = skillsService;
        _referenceDataService = referenceDataService;
        _portalNavigator = portalNavigator;
    }

    [HttpGet("Profile")]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken)
    {
        if (TryBuildJobsPortalRedirect(out var portalRedirect))
        {
            return Redirect(portalRedirect);
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var state = await _profileService.GetOnboardingStateAsync(userId, cancellationToken);
        if (state.Profile is null)
        {
            return RedirectToAction(nameof(Onboarding), new { step = 1 });
        }

        var skills = await _skillsService.GetSkillsForUserAsync(userId, cancellationToken);

        return View(new CandidateProfileOverviewViewModel
        {
            Profile = state.Profile,
            Completeness = state.CompletenessBreakdown,
            Skills = skills
        });
    }

    [HttpGet("Onboarding")]
    public async Task<IActionResult> Onboarding(short? step = null, CancellationToken cancellationToken = default)
    {
        if (TryBuildJobsPortalRedirect(out var portalRedirect))
        {
            return Redirect(portalRedirect);
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var state = await _profileService.GetOnboardingStateAsync(userId, cancellationToken);
        var resolvedStep = ResolveTargetStep(state, step);
        var viewModel = await BuildWizardViewModelAsync(state, resolvedStep, cancellationToken);

        return View(viewModel);
    }

    [HttpPost("Onboarding/Step1")]
    public async Task<IActionResult> SaveStep1(
        [Bind(Prefix = "Step1")] Step1BasicsInputModel input,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        if (!ModelState.IsValid)
        {
            var invalidState = await _profileService.GetOnboardingStateAsync(userId, cancellationToken);
            var invalidVm = await BuildWizardViewModelAsync(invalidState, 1, cancellationToken, step1Override: input);
            return View("Onboarding", invalidVm);
        }

        var result = await _profileService.SaveStep1BasicsAsync(
            new SaveCandidateBasicsCommand(
                UserId: userId,
                FullName: input.FullName,
                Headline: input.Headline,
                PrimaryCategory: input.PrimaryCategory,
                CityId: input.CityId,
                Bio: input.Bio),
            cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error.Message);
            var failedState = await _profileService.GetOnboardingStateAsync(userId, cancellationToken);
            var failedVm = await BuildWizardViewModelAsync(failedState, 1, cancellationToken, step1Override: input);
            return View("Onboarding", failedVm);
        }

        TempData["StatusMessage"] = "Step 1 (Basics) saved as draft.";
        return RedirectToAction(nameof(Onboarding), new { step = 2 });
    }

    [HttpPost("Onboarding/Step2")]
    public async Task<IActionResult> SaveStep2(
        [Bind(Prefix = "Step2")] Step2ExperienceCoreInputModel input,
        short nextStep = 3,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        if (!ModelState.IsValid)
        {
            var invalidState = await _profileService.GetOnboardingStateAsync(userId, cancellationToken);
            var invalidVm = await BuildWizardViewModelAsync(invalidState, 2, cancellationToken, step2Override: input);
            return View("Onboarding", invalidVm);
        }

        var result = await _profileService.SaveStep2ExperienceCoreAsync(
            new SaveCandidateExperienceCoreCommand(
                UserId: userId,
                YearsExperience: input.YearsExperience,
                Seniority: input.Seniority),
            cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error.Message);
            var failedState = await _profileService.GetOnboardingStateAsync(userId, cancellationToken);
            var failedVm = await BuildWizardViewModelAsync(failedState, 2, cancellationToken, step2Override: input);
            return View("Onboarding", failedVm);
        }

        var sanitizedNext = nextStep == 5 ? (short)5 : (short)3;
        TempData["StatusMessage"] = "Step 2 (Experience level) saved as draft.";
        return RedirectToAction(nameof(Onboarding), new { step = sanitizedNext });
    }

    [HttpPost("Onboarding/Step3")]
    public async Task<IActionResult> SaveStep3(
        [Bind(Prefix = "Step3")] Step3SkillsInputModel? input,
        short nextStep = 4,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var normalizedInput = input ?? new Step3SkillsInputModel();
        var selections = normalizedInput.Skills
            .Where(s => s.SkillId > 0)
            .Select(s => new CandidateSkillSelectionInput(
                SkillId: s.SkillId,
                Proficiency: s.Proficiency,
                YearsUsed: s.YearsUsed,
                LastUsedYear: s.LastUsedYear,
                IsPrimary: s.IsPrimary))
            .ToArray();

        var result = await _skillsService.SaveSkillsAsync(
            new SaveCandidateSkillsCommand(userId, selections),
            cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error.Message);
            var failedState = await _profileService.GetOnboardingStateAsync(userId, cancellationToken);
            var failedVm = await BuildWizardViewModelAsync(failedState, 3, cancellationToken, step3Override: normalizedInput);
            return View("Onboarding", failedVm);
        }

        var sanitizedNext = nextStep == 5 ? (short)5 : (short)4;
        TempData["StatusMessage"] = $"Saved {result.Value.Count} skills to your profile.";
        return RedirectToAction(nameof(Onboarding), new { step = sanitizedNext });
    }

    [HttpPost("Onboarding/Step5")]
    public async Task<IActionResult> SaveStep5(
        [Bind(Prefix = "Step5")] Step5PreferencesInputModel input,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        if (!ModelState.IsValid)
        {
            var invalidState = await _profileService.GetOnboardingStateAsync(userId, cancellationToken);
            var invalidVm = await BuildWizardViewModelAsync(invalidState, 5, cancellationToken, step5Override: input);
            return View("Onboarding", invalidVm);
        }

        var result = await _profileService.SaveStep5PreferencesAsync(
            new SaveCandidatePreferencesCommand(
                UserId: userId,
                ExpectedCtcLpa: input.ExpectedCtcLpa!.Value,
                CurrentCtcLpa: input.CurrentCtcLpa,
                ShowCurrentCtc: input.ShowCurrentCtc,
                NoticePeriodDays: input.NoticePeriodDays!.Value,
                OpenToRemote: input.OpenToRemote,
                WillingToRelocate: input.WillingToRelocate,
                Availability: input.Availability),
            cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error.Message);
            var failedState = await _profileService.GetOnboardingStateAsync(userId, cancellationToken);
            var failedVm = await BuildWizardViewModelAsync(failedState, 5, cancellationToken, step5Override: input);
            return View("Onboarding", failedVm);
        }

        TempData["StatusMessage"] = "Preferences saved. Your core developer profile is live on your workspace.";
        return RedirectToAction(nameof(Profile));
    }

    private async Task<CandidateOnboardingWizardViewModel> BuildWizardViewModelAsync(
        CandidateOnboardingState state,
        short activeStep,
        CancellationToken cancellationToken,
        Step1BasicsInputModel? step1Override = null,
        Step2ExperienceCoreInputModel? step2Override = null,
        Step3SkillsInputModel? step3Override = null,
        Step5PreferencesInputModel? step5Override = null)
    {
        var cities = activeStep == 1
            ? await _referenceDataService.GetActiveCitiesAsync(cancellationToken)
            : [];

        IReadOnlyList<SkillLookupItem> availableSkills = [];
        Step3SkillsInputModel step3;

        if (activeStep == 3)
        {
            availableSkills = await _referenceDataService.GetActiveSkillsAsync(null, cancellationToken);
            var catalogById = availableSkills.ToDictionary(s => s.Id);

            if (step3Override is not null)
            {
                foreach (var row in step3Override.Skills)
                {
                    if (catalogById.TryGetValue(row.SkillId, out var skillMeta))
                    {
                        row.SkillName = skillMeta.Name;
                        row.CategoryLabel = FormatCategoryLabel(skillMeta.Category);
                    }
                }

                step3 = step3Override;
            }
            else
            {
                var savedSkills = await _skillsService.GetSkillsForUserAsync(state.UserId, cancellationToken);
                step3 = new Step3SkillsInputModel
                {
                    Skills = savedSkills.Select(s => new CandidateSkillInputRow
                    {
                        SkillId = s.SkillId,
                        SkillName = s.SkillName,
                        CategoryLabel = FormatCategoryLabel(s.Category),
                        Proficiency = s.Proficiency,
                        YearsUsed = s.YearsUsed,
                        LastUsedYear = s.LastUsedYear,
                        IsPrimary = s.IsPrimary,
                        EvidenceLevel = s.EvidenceLevel
                    }).ToList()
                };
            }
        }
        else
        {
            step3 = step3Override ?? new Step3SkillsInputModel();
        }

        var profile = state.Profile;

        var step1 = step1Override ?? new Step1BasicsInputModel
        {
            FullName = profile?.FullName ?? state.SuggestedFullName,
            Headline = profile?.Headline ?? string.Empty,
            PrimaryCategory = profile?.PrimaryCategory ?? DisciplineCategory.Programming,
            CityId = profile?.CityId ?? 0,
            Bio = profile?.Bio
        };

        var defaultYears = profile?.YearsExperience ?? 0.0m;
        var step2 = step2Override ?? new Step2ExperienceCoreInputModel
        {
            YearsExperience = defaultYears,
            Seniority = profile?.Seniority ?? CandidateProfileRules.SuggestSeniorityFromYears(defaultYears)
        };

        var step5 = step5Override ?? new Step5PreferencesInputModel
        {
            ExpectedCtcLpa = profile?.ExpectedCtcLpa,
            CurrentCtcLpa = profile?.CurrentCtcLpa,
            ShowCurrentCtc = profile?.ShowCurrentCtc ?? false,
            NoticePeriodDays = profile?.NoticePeriodDays ?? 30,
            OpenToRemote = profile?.OpenToRemote ?? true,
            WillingToRelocate = profile?.WillingToRelocate ?? false,
            Availability = profile?.Availability ?? CandidateAvailability.ActivelyLooking
        };

        return new CandidateOnboardingWizardViewModel
        {
            ActiveStep = activeStep,
            HasExistingProfile = profile is not null,
            OnboardingState = state,
            Cities = cities,
            AvailableSkills = availableSkills,
            Step1 = step1,
            Step2 = step2,
            Step3 = step3,
            Step5 = step5
        };
    }

    internal static string FormatCategoryLabel(SkillCategory category)
    {
        return category switch
        {
            SkillCategory.Engine => "Engine",
            SkillCategory.Language => "Language",
            SkillCategory.ArtTool => "Art Tool",
            SkillCategory.DesignAudioTool => "Design & Audio",
            SkillCategory.Devops => "DevOps",
            SkillCategory.Genre => "Genre",
            SkillCategory.Platform => "Platform",
            SkillCategory.Role => "Discipline",
            _ => category.ToString()
        };
    }

    private static short ResolveTargetStep(CandidateOnboardingState state, short? requestedStep)
    {
        if (state.Profile is null)
        {
            return 1;
        }

        if (requestedStep is >= 1 and <= 5)
        {
            return requestedStep.Value;
        }

        return Math.Clamp(state.Profile.OnboardingStep, (short)1, (short)5);
    }

    private bool TryBuildJobsPortalRedirect(out string redirectUrl)
    {
        redirectUrl = string.Empty;
        if (!HttpMethods.IsGet(Request.Method))
        {
            return false;
        }

        var portal = _portalNavigator.ResolvePortal(HttpContext);
        if (portal == PortalSite.Jobs || _portalNavigator.DeploymentMode != PortalDeploymentMode.All)
        {
            return false;
        }

        if (portal == PortalSite.Studio || _portalNavigator.ResolveSharedCookieDomain(HttpContext) is not null)
        {
            var pathAndQuery = $"{Request.Path}{Request.QueryString}";
            redirectUrl = _portalNavigator.BuildPortalUrl(PortalSite.Jobs, HttpContext, pathAndQuery);
            return true;
        }

        return false;
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) &&
               userId != Guid.Empty;
    }
}
