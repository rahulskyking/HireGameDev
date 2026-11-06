using System.Security.Claims;
using HireGameDev.Application.Abstractions.Candidates;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Features.Candidates;
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
    private readonly IReferenceDataService _referenceDataService;
    private readonly IPortalNavigator _portalNavigator;

    public CandidateController(
        ICandidateProfileService profileService,
        IReferenceDataService referenceDataService,
        IPortalNavigator portalNavigator)
    {
        _profileService = profileService;
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

        return View(new CandidateProfileOverviewViewModel
        {
            Profile = state.Profile,
            Completeness = state.CompletenessBreakdown
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
        Step5PreferencesInputModel? step5Override = null)
    {
        var cities = activeStep == 1
            ? await _referenceDataService.GetActiveCitiesAsync(cancellationToken)
            : [];

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
            Step1 = step1,
            Step2 = step2,
            Step5 = step5
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

        // Redirect from studio.* always, and from apex domain when shared cross-subdomain cookies are active (.hiregamedev.com / .lvh.me).
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
