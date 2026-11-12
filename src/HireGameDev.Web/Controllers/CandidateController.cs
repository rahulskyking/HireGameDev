using System.Security.Claims;
using HireGameDev.Application.Abstractions.Candidates;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Abstractions.Storage;
using HireGameDev.Application.Features.Candidates;
using HireGameDev.Application.Features.ReferenceData;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Web.Authorization;
using HireGameDev.Web.Models.Candidate;
using HireGameDev.Web.Portals;
using HireGameDev.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HireGameDev.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.CandidateOnly)]
[Route("Candidate")]
public sealed class CandidateController : Controller
{
    private readonly ICandidateProfileService _profileService;
    private readonly ICandidatePhotoService _photoService;
    private readonly ICandidateSkillsService _skillsService;
    private readonly ICandidateBackgroundService _backgroundService;
    private readonly ICandidateProofService _proofService;
    private readonly ICandidateResumeImportService _resumeImportService;
    private readonly ICandidateSettingsService _settingsService;
    private readonly IFileStorageService _fileStorageService;
    private readonly IReferenceDataService _referenceDataService;
    private readonly IPortalNavigator _portalNavigator;

    public CandidateController(
        ICandidateProfileService profileService,
        ICandidatePhotoService photoService,
        ICandidateSkillsService skillsService,
        ICandidateBackgroundService backgroundService,
        ICandidateProofService proofService,
        ICandidateResumeImportService resumeImportService,
        ICandidateSettingsService settingsService,
        IFileStorageService fileStorageService,
        IReferenceDataService referenceDataService,
        IPortalNavigator portalNavigator)
    {
        _profileService = profileService;
        _photoService = photoService;
        _skillsService = skillsService;
        _backgroundService = backgroundService;
        _proofService = proofService;
        _resumeImportService = resumeImportService;
        _settingsService = settingsService;
        _fileStorageService = fileStorageService;
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
        var background = await _backgroundService.GetBackgroundForUserAsync(userId, cancellationToken);
        var ownerProof = await _proofService.GetOwnerProofForUserAsync(userId, cancellationToken);
        var publicProof = await _proofService.GetPublicProofForCandidateAsync(state.Profile.Id, cancellationToken);
        var settingsResult = await _settingsService.GetSettingsAsync(userId, cancellationToken);
        var blockedStudiosCount = settingsResult.IsSuccess ? settingsResult.Value.BlockedStudios.Count : 0;

        return View(new CandidateProfileOverviewViewModel
        {
            Profile = state.Profile,
            Completeness = state.CompletenessBreakdown,
            Skills = skills,
            Experiences = background.Experiences,
            Education = background.Education,
            OwnerProof = ownerProof,
            PublicProof = publicProof,
            BlockedStudiosCount = blockedStudiosCount
        });
    }

    [HttpGet("Profile/Edit")]
    public IActionResult EditProfile(short? step = null)
    {
        if (TryBuildJobsPortalRedirect(out var portalRedirect))
        {
            return Redirect(portalRedirect);
        }

        return RedirectToAction(nameof(Onboarding), new { step = step ?? 1 });
    }

    [HttpGet("Profile/Preview")]
    public async Task<IActionResult> Preview(CancellationToken cancellationToken)
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
            TempData["ErrorMessage"] = "Complete Step 1 (Basics) before previewing how studios see your profile.";
            return RedirectToAction(nameof(Onboarding), new { step = 1 });
        }

        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers["Cache-Control"] = "private, no-store";

        var skills = await _skillsService.GetSkillsForUserAsync(userId, cancellationToken);
        var background = await _backgroundService.GetBackgroundForUserAsync(userId, cancellationToken);
        var publicProof = await _proofService.GetPublicProofForCandidateAsync(state.Profile.Id, cancellationToken);
        var settingsResult = await _settingsService.GetSettingsAsync(userId, cancellationToken);
        var blockedStudiosCount = settingsResult.IsSuccess ? settingsResult.Value.BlockedStudios.Count : 0;

        return View(new CandidateProfilePreviewViewModel
        {
            Profile = state.Profile,
            Completeness = state.CompletenessBreakdown,
            Skills = skills,
            Experiences = background.Experiences,
            Education = background.Education,
            PublicProof = publicProof,
            BlockedStudiosCount = blockedStudiosCount
        });
    }

    [HttpGet("Settings")]
    public async Task<IActionResult> Settings(
        string? studioQuery = null,
        CancellationToken cancellationToken = default)
    {
        if (TryBuildJobsPortalRedirect(out var portalRedirect))
        {
            return Redirect(portalRedirect);
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var settingsResult = await _settingsService.GetSettingsAsync(userId, cancellationToken);
        if (settingsResult.IsFailure)
        {
            TempData["ErrorMessage"] = settingsResult.Error.Message;
            return RedirectToAction(nameof(Onboarding), new { step = 1 });
        }

        var viewModel = await BuildSettingsPageViewModelAsync(
            userId,
            settingsResult.Value,
            studioQuery,
            privacyFormOverride: null,
            cancellationToken);

        return View(viewModel);
    }

    [HttpPost("Settings/Privacy")]
    [EnableRateLimiting(RateLimitPolicies.CandidateSettingsWrite)]
    public async Task<IActionResult> UpdatePrivacySettings(
        [Bind(Prefix = "PrivacyForm")] UpdateCandidatePrivacyInputModel input,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        if (!ModelState.IsValid)
        {
            var currentSettings = await _settingsService.GetSettingsAsync(userId, cancellationToken);
            if (currentSettings.IsFailure)
            {
                return RedirectToAction(nameof(Onboarding), new { step = 1 });
            }

            var invalidVm = await BuildSettingsPageViewModelAsync(
                userId,
                currentSettings.Value,
                studioQuery: null,
                privacyFormOverride: input,
                cancellationToken);

            return View("Settings", invalidVm);
        }

        var updateResult = await _settingsService.UpdatePrivacySettingsAsync(
            new UpdateCandidatePrivacySettingsCommand(
                UserId: userId,
                IsDiscoverable: input.IsDiscoverable,
                Availability: input.Availability,
                CurrentCtcLpa: input.CurrentCtcLpa,
                ShowCurrentCtc: input.ShowCurrentCtc),
            cancellationToken);

        if (updateResult.IsFailure)
        {
            ModelState.AddModelError(string.Empty, updateResult.Error.Message);
            var currentSettings = await _settingsService.GetSettingsAsync(userId, cancellationToken);
            if (currentSettings.IsFailure)
            {
                return RedirectToAction(nameof(Onboarding), new { step = 1 });
            }

            var failedVm = await BuildSettingsPageViewModelAsync(
                userId,
                currentSettings.Value,
                studioQuery: null,
                privacyFormOverride: input,
                cancellationToken);

            return View("Settings", failedVm);
        }

        TempData["StatusMessage"] = "Discoverability and compensation privacy settings updated.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpGet("Settings/Studios/Search")]
    public async Task<IActionResult> SearchBlockableStudios(
        string? q = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await _settingsService.SearchStudiosToBlockAsync(userId, q, cancellationToken);
        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error.Message });
        }

        var items = result.Value.Select(s => new
        {
            studioId = s.StudioId,
            name = s.Name,
            slug = s.Slug,
            logoUrl = ResolveStudioLogoUrlOrNull(s.LogoPath),
            publicUrl = _portalNavigator.BuildPortalUrl(PortalSite.Jobs, HttpContext, $"/studios/{s.Slug}"),
            location = FormatStudioLocation(s.CityName, s.StateCode),
            studioTypeLabel = s.StudioType.ToDisplayLabel(),
            isAlreadyBlocked = s.IsAlreadyBlocked
        });

        return Json(new { items });
    }

    [HttpPost("Settings/Studios/Block")]
    [EnableRateLimiting(RateLimitPolicies.CandidateSettingsWrite)]
    public async Task<IActionResult> BlockStudio(
        Guid studioId,
        string? studioQuery = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var result = await _settingsService.BlockStudioAsync(
            new BlockStudioCommand(userId, studioId),
            cancellationToken);

        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = result.Error.Message;
            return string.IsNullOrWhiteSpace(studioQuery)
                ? RedirectToAction(nameof(Settings))
                : RedirectToAction(nameof(Settings), new { studioQuery = studioQuery.Trim() });
        }

        TempData["StatusMessage"] = $"Your profile is now hidden from {result.Value.Name} in studio searches and unsolicited views.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost("Settings/Studios/Unblock")]
    [EnableRateLimiting(RateLimitPolicies.CandidateSettingsWrite)]
    public async Task<IActionResult> UnblockStudio(
        Guid studioId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var result = await _settingsService.UnblockStudioAsync(
            new UnblockStudioCommand(userId, studioId),
            cancellationToken);

        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = result.Error.Message;
            return RedirectToAction(nameof(Settings));
        }

        TempData["StatusMessage"] = "Studio removed from your blocklist.";
        return RedirectToAction(nameof(Settings));
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
    [RequestSizeLimit(ImageUploadRules.MaxMultipartRequestBytes)]
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

        if (input.AvatarFile is not null && input.AvatarFile.Length > 0)
        {
            await using var avatarStream = input.AvatarFile.OpenReadStream();
            var photoResult = await _photoService.UploadPhotoAsync(
                new UploadCandidatePhotoCommand(
                    UserId: userId,
                    Content: avatarStream,
                    FileName: input.AvatarFile.FileName,
                    DeclaredContentType: input.AvatarFile.ContentType),
                cancellationToken);

            if (photoResult.IsFailure)
            {
                ModelState.AddModelError(string.Empty, photoResult.Error.Message);
                var photoFailedState = await _profileService.GetOnboardingStateAsync(userId, cancellationToken);
                var photoFailedVm = await BuildWizardViewModelAsync(photoFailedState, 1, cancellationToken, step1Override: input);
                return View("Onboarding", photoFailedVm);
            }

            TempData["StatusMessage"] = "Step 1 (Basics) and profile photo saved.";
            return RedirectToAction(nameof(Onboarding), new { step = 2 });
        }

        TempData["StatusMessage"] = "Step 1 (Basics) saved as draft.";
        return RedirectToAction(nameof(Onboarding), new { step = 2 });
    }

    [HttpPost("Profile/Photo")]
    [RequestSizeLimit(ImageUploadRules.MaxMultipartRequestBytes)]
    public async Task<IActionResult> UploadPhoto(
        IFormFile? avatarFile,
        string? returnTo = "profile",
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        if (avatarFile is null || avatarFile.Length == 0)
        {
            TempData["ErrorMessage"] = "Select a JPG, PNG, or WebP image (up to 1 MB) before uploading.";
            return RedirectAfterPhotoAction(returnTo);
        }

        await using var stream = avatarFile.OpenReadStream();
        var result = await _photoService.UploadPhotoAsync(
            new UploadCandidatePhotoCommand(
                UserId: userId,
                Content: stream,
                FileName: avatarFile.FileName,
                DeclaredContentType: avatarFile.ContentType),
            cancellationToken);

        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = result.Error.Message;
            return RedirectAfterPhotoAction(returnTo);
        }

        TempData["StatusMessage"] = $"Profile photo updated (completeness: {result.Value.Completeness}%).";
        return RedirectAfterPhotoAction(returnTo);
    }

    [HttpPost("Profile/Photo/Remove")]
    public async Task<IActionResult> RemovePhoto(
        string? returnTo = "profile",
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var result = await _photoService.RemovePhotoAsync(userId, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = result.Error.Message;
            return RedirectAfterPhotoAction(returnTo);
        }

        TempData["StatusMessage"] = $"Profile photo removed (completeness: {result.Value.Completeness}%).";
        return RedirectAfterPhotoAction(returnTo);
    }

    private IActionResult RedirectAfterPhotoAction(string? returnTo)
    {
        if (string.Equals(returnTo, "step1", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction(nameof(Onboarding), new { step = 1 });
        }

        return RedirectToAction(nameof(Profile));
    }

    [HttpGet("Resume/Import")]
    public async Task<IActionResult> ResumeImport(CancellationToken cancellationToken)
    {
        if (TryBuildJobsPortalRedirect(out var portalRedirect))
        {
            return Redirect(portalRedirect);
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var vm = await BuildResumeImportViewModelAsync(userId, reviewOverride: null, draft: null, cancellationToken);
        return View("ResumeImport", vm);
    }

    [HttpPost("Resume/Import/Parse")]
    [EnableRateLimiting(RateLimitPolicies.ResumeParse)]
    [RequestSizeLimit(ResumePdfUploadRules.MaxMultipartRequestBytes)]
    public async Task<IActionResult> ParseResume(
        IFormFile? resumePdf,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        if (resumePdf is null || resumePdf.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Select a Resume or LinkedIn PDF file (up to 2 MB) to import.");
            var emptyVm = await BuildResumeImportViewModelAsync(userId, reviewOverride: null, draft: null, cancellationToken);
            return View("ResumeImport", emptyVm);
        }

        if (resumePdf.Length > ResumePdfUploadRules.MaxPdfUploadBytes)
        {
            ModelState.AddModelError(string.Empty, "Resume PDF must be 2 MB or smaller.");
            var largeVm = await BuildResumeImportViewModelAsync(userId, reviewOverride: null, draft: null, cancellationToken);
            return View("ResumeImport", largeVm);
        }

        await using var pdfStream = resumePdf.OpenReadStream();
        var parseResult = await _resumeImportService.ParseResumePdfAsync(
            new ParseCandidateResumeCommand(
                UserId: userId,
                PdfStream: pdfStream,
                FileName: resumePdf.FileName),
            cancellationToken);

        if (parseResult.IsFailure)
        {
            ModelState.AddModelError(string.Empty, parseResult.Error.Message);
            var failedVm = await BuildResumeImportViewModelAsync(userId, reviewOverride: null, draft: null, cancellationToken);
            return View("ResumeImport", failedVm);
        }

        var successVm = await BuildResumeImportViewModelAsync(
            userId,
            reviewOverride: null,
            draft: parseResult.Value,
            cancellationToken);

        return View("ResumeImport", successVm);
    }

    [HttpPost("Resume/Import/Apply")]
    public async Task<IActionResult> ApplyResumeImport(
        [Bind(Prefix = "Review")] ResumeDraftConfirmInputModel? input,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var normalized = input ?? new ResumeDraftConfirmInputModel();
        var state = await _profileService.GetOnboardingStateAsync(userId, cancellationToken);

        // If the candidate has not created their Step 1 profile yet, create it seamlessly from the Basics card.
        if (state.Profile is null)
        {
            var resolvedHeadline = normalized.ApplyHeadline && !string.IsNullOrWhiteSpace(normalized.Headline)
                ? normalized.Headline.Trim()
                : $"{normalized.PrimaryCategory} Developer";

            var step1Result = await _profileService.SaveStep1BasicsAsync(
                new SaveCandidateBasicsCommand(
                    UserId: userId,
                    FullName: normalized.FullName,
                    Headline: resolvedHeadline,
                    PrimaryCategory: normalized.PrimaryCategory,
                    CityId: normalized.CityId,
                    Bio: normalized.ApplyBio && !string.IsNullOrWhiteSpace(normalized.Bio)
                        ? normalized.Bio.Trim()
                        : null),
                cancellationToken);

            if (step1Result.IsFailure)
            {
                ModelState.AddModelError(string.Empty, step1Result.Error.Message);
                var step1FailedVm = await BuildResumeImportViewModelAsync(
                    userId,
                    reviewOverride: normalized,
                    draft: null,
                    cancellationToken,
                    preloadedState: state);
                return View("ResumeImport", step1FailedVm);
            }
        }

        var selectedSkills = new List<ConfirmImportedSkillInput>();
        for (var i = 0; i < normalized.Skills.Count; i++)
        {
            var s = normalized.Skills[i];
            if (!s.Include || s.SkillId <= 0)
            {
                continue;
            }

            selectedSkills.Add(new ConfirmImportedSkillInput(
                SkillId: s.SkillId,
                Proficiency: s.Proficiency,
                YearsUsed: s.YearsUsed,
                LastUsedYear: s.LastUsedYear,
                IsPrimary: s.IsPrimary));
        }

        var selectedExperiences = new List<ConfirmImportedExperienceInput>();
        for (var i = 0; i < normalized.Experiences.Count; i++)
        {
            var exp = normalized.Experiences[i];
            if (!exp.Include)
            {
                continue;
            }

            if (!exp.StartDate.HasValue)
            {
                ModelState.AddModelError(
                    string.Empty,
                    $"Work Experience #{i + 1}: Start date is required (or uncheck that entry).");
                var dateErrVm = await BuildResumeImportViewModelAsync(userId, normalized, null, cancellationToken);
                return View("ResumeImport", dateErrVm);
            }

            selectedExperiences.Add(new ConfirmImportedExperienceInput(
                CompanyName: exp.CompanyName,
                Title: exp.Title,
                StartDate: exp.StartDate.Value,
                EndDate: exp.IsCurrent ? null : exp.EndDate,
                IsCurrent: exp.IsCurrent,
                Description: exp.Description));
        }

        var selectedEducation = new List<ConfirmImportedEducationInput>();
        for (var i = 0; i < normalized.Education.Count; i++)
        {
            var edu = normalized.Education[i];
            if (!edu.Include)
            {
                continue;
            }

            selectedEducation.Add(new ConfirmImportedEducationInput(
                Institution: edu.Institution,
                Degree: edu.Degree,
                Field: edu.Field,
                StartYear: edu.StartYear,
                EndYear: edu.EndYear));
        }

        var selectedTitles = new List<ConfirmImportedShippedTitleInput>();
        for (var i = 0; i < normalized.ShippedTitles.Count; i++)
        {
            var title = normalized.ShippedTitles[i];
            if (!title.Include)
            {
                continue;
            }

            selectedTitles.Add(new ConfirmImportedShippedTitleInput(
                GameName: title.GameName,
                StudioName: title.StudioName,
                Role: title.Role,
                Platforms: title.Platforms,
                EngineSkillId: title.EngineSkillId,
                ReleaseYear: title.ReleaseYear));
        }

        var applyResult = await _resumeImportService.ApplyImportedDraftAsync(
            new ApplyCandidateResumeDraftCommand(
                UserId: userId,
                ApplyHeadline: normalized.ApplyHeadline && !string.IsNullOrWhiteSpace(normalized.Headline),
                Headline: normalized.Headline,
                ApplyYearsExperience: normalized.ApplyYearsExperience && normalized.YearsExperience.HasValue,
                YearsExperience: normalized.YearsExperience,
                Skills: selectedSkills,
                Experiences: selectedExperiences,
                Education: selectedEducation,
                ShippedTitles: selectedTitles,
                ApplyBio: normalized.ApplyBio && !string.IsNullOrWhiteSpace(normalized.Bio),
                Bio: normalized.Bio),
            cancellationToken);

        if (applyResult.IsFailure)
        {
            ModelState.AddModelError(string.Empty, applyResult.Error.Message);
            var applyErrVm = await BuildResumeImportViewModelAsync(userId, normalized, null, cancellationToken);
            return View("ResumeImport", applyErrVm);
        }

        var r = applyResult.Value;
        TempData["StatusMessage"] =
            $"Resume import applied: +{r.AddedSkillCount} skills, +{r.AddedExperienceCount} experience, " +
            $"+{r.AddedEducationCount} education, +{r.AddedShippedTitleCount} shipped titles (completeness: {r.UpdatedCompleteness}%).";

        return RedirectToAction(nameof(Profile));
    }

    private async Task<ResumeImportPageViewModel> BuildResumeImportViewModelAsync(
        Guid userId,
        ResumeDraftConfirmInputModel? reviewOverride,
        ResumeImportDraft? draft,
        CancellationToken cancellationToken,
        CandidateOnboardingState? preloadedState = null)
    {
        var state = preloadedState ?? await _profileService.GetOnboardingStateAsync(userId, cancellationToken);
        var quota = draft?.QuotaAfterParse ?? await _resumeImportService.GetQuotaStatusAsync(userId, cancellationToken);
        var hasExistingProfile = state.Profile is not null;

        IReadOnlyList<CityLookupItem> cities = hasExistingProfile
            ? []
            : await _referenceDataService.GetActiveCitiesAsync(cancellationToken);

        var engineSkills = await _referenceDataService.GetActiveSkillsAsync(SkillCategory.Engine, cancellationToken);

        ResumeDraftConfirmInputModel review;
        if (reviewOverride is not null)
        {
            review = reviewOverride;
        }
        else if (draft is not null)
        {
            var suggestedFullName = state.Profile?.FullName
                ?? (!string.IsNullOrWhiteSpace(draft.SuggestedFullName) ? draft.SuggestedFullName : state.SuggestedFullName);

            review = new ResumeDraftConfirmInputModel
            {
                FullName = suggestedFullName,
                PrimaryCategory = state.Profile?.PrimaryCategory
                    ?? draft.SuggestedDiscipline
                    ?? DisciplineCategory.Programming,
                CityId = state.Profile?.CityId
                    ?? draft.SuggestedCityId
                    ?? 0,
                ApplyHeadline = !string.IsNullOrWhiteSpace(draft.Headline),
                Headline = draft.Headline,
                ApplyBio = !string.IsNullOrWhiteSpace(draft.Bio),
                Bio = draft.Bio,
                ApplyYearsExperience = draft.YearsExperience.HasValue,
                YearsExperience = draft.YearsExperience,
                Skills = draft.MatchedSkills.Select(s => new ResumeDraftSkillRowInputModel
                {
                    Include = true,
                    SkillId = s.SkillId,
                    SkillName = s.SkillName,
                    Category = s.Category,
                    MatchKind = s.MatchKind,
                    RawExtractedName = s.RawExtractedName,
                    Proficiency = s.SuggestedProficiency,
                    YearsUsed = s.YearsUsed,
                    LastUsedYear = s.LastUsedYear,
                    IsPrimary = s.SuggestedPrimary
                }).ToList(),
                UnmatchedSkillSuggestions = draft.UnmatchedSkillSuggestions.ToList(),
                Experiences = draft.Experiences.Select(e => new ResumeDraftExperienceRowInputModel
                {
                    Include = true,
                    CompanyName = e.CompanyName,
                    Title = e.Title,
                    StartDate = e.StartDate,
                    EndDate = e.EndDate,
                    IsCurrent = e.IsCurrent,
                    Description = e.Description
                }).ToList(),
                Education = draft.Education.Select(ed => new ResumeDraftEducationRowInputModel
                {
                    Include = true,
                    Institution = ed.Institution,
                    Degree = ed.Degree,
                    Field = ed.Field,
                    StartYear = ed.StartYear,
                    EndYear = ed.EndYear
                }).ToList(),
                ShippedTitles = draft.ShippedTitles.Select(st => new ResumeDraftShippedTitleRowInputModel
                {
                    Include = true,
                    GameName = st.GameName,
                    StudioName = st.StudioName ?? string.Empty,
                    Role = st.Role ?? string.Empty,
                    ReleaseYear = st.ReleaseYear,
                    EngineSkillId = st.MatchedEngineSkillId,
                    Platforms = st.Platforms?.ToList() ?? []
                }).ToList()
            };
        }
        else
        {
            review = new ResumeDraftConfirmInputModel
            {
                FullName = state.Profile?.FullName ?? state.SuggestedFullName,
                PrimaryCategory = state.Profile?.PrimaryCategory ?? DisciplineCategory.Programming,
                CityId = state.Profile?.CityId ?? 0
            };
        }

        return new ResumeImportPageViewModel
        {
            HasExistingProfile = hasExistingProfile,
            HasParsedDraft = draft is not null || reviewOverride is not null,
            Quota = quota,
            DetectedSource = draft?.Source,
            ProviderName = draft?.ProviderName,
            Cities = cities,
            EngineSkills = engineSkills,
            Review = review
        };
    }

    [HttpPost("Onboarding/Step2")]
    public async Task<IActionResult> SaveStep2(
        [Bind(Prefix = "Step2")] Step2ExperienceCoreInputModel? input,
        short nextStep = 3,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var normalizedInput = input ?? new Step2ExperienceCoreInputModel();
        var nonEmptyExpRows = normalizedInput.Experiences
            .Where(e => !e.IsCompletelyEmpty)
            .ToList();
        var nonEmptyEduRows = normalizedInput.Education
            .Where(e => !e.IsCompletelyEmpty)
            .ToList();

        normalizedInput.Experiences = nonEmptyExpRows;
        normalizedInput.Education = nonEmptyEduRows;

        for (var i = 0; i < nonEmptyExpRows.Count; i++)
        {
            if (!nonEmptyExpRows[i].StartDate.HasValue)
            {
                ModelState.AddModelError(string.Empty, $"Select a start date for work experience entry #{i + 1}.");
                break;
            }
        }

        if (!ModelState.IsValid)
        {
            var invalidState = await _profileService.GetOnboardingStateAsync(userId, cancellationToken);
            var invalidVm = await BuildWizardViewModelAsync(invalidState, 2, cancellationToken, step2Override: normalizedInput);
            return View("Onboarding", invalidVm);
        }

        var experienceInputs = nonEmptyExpRows
            .Select(e => new CandidateExperienceInput(
                Id: e.Id,
                CompanyName: e.CompanyName,
                Title: e.Title,
                StartDate: e.StartDate!.Value,
                EndDate: e.IsCurrent ? null : e.EndDate,
                IsCurrent: e.IsCurrent,
                Description: e.Description))
            .ToArray();

        var educationInputs = nonEmptyEduRows
            .Select(e => new CandidateEducationInput(
                Id: e.Id,
                Institution: e.Institution,
                Degree: e.Degree,
                Field: e.Field,
                StartYear: e.StartYear,
                EndYear: e.EndYear))
            .ToArray();

        var result = await _backgroundService.SaveStep2BackgroundAsync(
            new SaveCandidateStep2BackgroundCommand(
                UserId: userId,
                YearsExperience: normalizedInput.YearsExperience,
                Seniority: normalizedInput.Seniority,
                Experiences: experienceInputs,
                Education: educationInputs),
            cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error.Message);
            var failedState = await _profileService.GetOnboardingStateAsync(userId, cancellationToken);
            var failedVm = await BuildWizardViewModelAsync(failedState, 2, cancellationToken, step2Override: normalizedInput);
            return View("Onboarding", failedVm);
        }

        var sanitizedNext = nextStep == 5 ? (short)5 : (short)3;
        TempData["StatusMessage"] = $"Step 2 saved ({result.Value.Experiences.Count} experience, {result.Value.Education.Count} education).";
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

    [HttpPost("Onboarding/Step4")]
    public async Task<IActionResult> SaveStep4(
        [Bind(Prefix = "Step4")] Step4ProofInputModel? input,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var normalizedInput = input ?? new Step4ProofInputModel();
        var nonEmptyTitles = normalizedInput.ShippedTitles
            .Where(t => !t.IsCompletelyEmpty)
            .ToList();
        var nonEmptyLinks = normalizedInput.PortfolioLinks
            .Where(l => !l.IsCompletelyEmpty)
            .ToList();

        normalizedInput.ShippedTitles = nonEmptyTitles;
        normalizedInput.PortfolioLinks = nonEmptyLinks;

        // Verification is strictly backend-controlled and intentionally omitted from write inputs.
        var titleInputs = nonEmptyTitles
            .Select(t => new SaveShippedTitleInput(
                Id: t.Id,
                GameName: t.GameName,
                StudioName: t.StudioName,
                Role: t.Role,
                Platforms: t.Platforms.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray(),
                EngineSkillId: t.EngineSkillId is > 0 ? t.EngineSkillId : null,
                ReleaseYear: t.ReleaseYear,
                StoreUrl: t.StoreUrl,
                IsNda: t.IsNda,
                HideStudioInPublic: t.IsNda && t.HideStudioInPublic))
            .ToArray();

        var linkInputs = nonEmptyLinks
            .Select(l => new SavePortfolioLinkInput(
                Id: l.Id,
                LinkType: l.LinkType,
                Url: l.Url,
                Title: l.Title,
                SkillIds: l.SkillIds.Where(id => id > 0).Distinct().ToArray()))
            .ToArray();

        var result = await _proofService.SaveStep4ProofAsync(
            new SaveCandidateStep4ProofCommand(
                UserId: userId,
                ShippedTitles: titleInputs,
                PortfolioLinks: linkInputs),
            cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error.Message);
            var failedState = await _profileService.GetOnboardingStateAsync(userId, cancellationToken);
            var failedVm = await BuildWizardViewModelAsync(failedState, 4, cancellationToken, step4Override: normalizedInput);
            return View("Onboarding", failedVm);
        }

        TempData["StatusMessage"] = $"Step 4 saved ({result.Value.ShippedTitles.Count} shipped credits, {result.Value.PortfolioLinks.Count} portfolio links).";
        return RedirectToAction(nameof(Onboarding), new { step = 5 });
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
        Step4ProofInputModel? step4Override = null,
        Step5PreferencesInputModel? step5Override = null)
    {
        var cities = activeStep == 1
            ? await _referenceDataService.GetActiveCitiesAsync(cancellationToken)
            : [];

        IReadOnlyList<SkillLookupItem> availableSkills = [];
        IReadOnlyList<SkillLookupItem> engineSkills = [];
        IReadOnlyList<CandidateSkillItem> candidateSkillsForTagging = [];
        Step3SkillsInputModel step3;
        Step4ProofInputModel step4;

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

        if (activeStep == 4)
        {
            engineSkills = await _referenceDataService.GetActiveSkillsAsync(SkillCategory.Engine, cancellationToken);
            candidateSkillsForTagging = await _skillsService.GetSkillsForUserAsync(state.UserId, cancellationToken);
            var savedProof = await _proofService.GetOwnerProofForUserAsync(state.UserId, cancellationToken);

            if (step4Override is not null)
            {
                var existingVerifications = savedProof.ShippedTitles.ToDictionary(t => t.Id, t => t.Verification);
                foreach (var row in step4Override.ShippedTitles)
                {
                    if (row.Id.HasValue && existingVerifications.TryGetValue(row.Id.Value, out var existingVerification))
                    {
                        row.Verification = existingVerification;
                    }
                }

                step4 = step4Override;
            }
            else
            {
                step4 = new Step4ProofInputModel
                {
                    ShippedTitles = savedProof.ShippedTitles.Select(t => new ShippedTitleInputRow
                    {
                        Id = t.Id,
                        GameName = t.GameName,
                        StudioName = t.StudioName,
                        Role = t.Role,
                        Platforms = t.Platforms.ToList(),
                        EngineSkillId = t.EngineSkillId,
                        ReleaseYear = t.ReleaseYear,
                        StoreUrl = t.StoreUrl,
                        IsNda = t.IsNda,
                        HideStudioInPublic = t.HideStudioInPublic,
                        Verification = t.Verification
                    }).ToList(),
                    PortfolioLinks = savedProof.PortfolioLinks.Select(l => new PortfolioLinkInputRow
                    {
                        Id = l.Id,
                        LinkType = l.LinkType,
                        Url = l.Url,
                        Title = l.Title,
                        SkillIds = l.EvidencedSkills.Select(s => s.SkillId).ToList()
                    }).ToList()
                };
            }
        }
        else
        {
            step4 = step4Override ?? new Step4ProofInputModel();
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
        Step2ExperienceCoreInputModel step2;

        if (step2Override is not null)
        {
            step2 = step2Override;
        }
        else if (activeStep == 2 && profile is not null)
        {
            var savedBg = await _backgroundService.GetBackgroundForUserAsync(state.UserId, cancellationToken);
            step2 = new Step2ExperienceCoreInputModel
            {
                YearsExperience = defaultYears,
                Seniority = profile.Seniority,
                Experiences = savedBg.Experiences.Select(e => new CandidateExperienceInputRow
                {
                    Id = e.Id,
                    CompanyName = e.CompanyName,
                    Title = e.Title,
                    StartDate = e.StartDate,
                    EndDate = e.EndDate,
                    IsCurrent = e.IsCurrent,
                    Description = e.Description
                }).ToList(),
                Education = savedBg.Education.Select(e => new CandidateEducationInputRow
                {
                    Id = e.Id,
                    Institution = e.Institution,
                    Degree = e.Degree,
                    Field = e.Field,
                    StartYear = e.StartYear,
                    EndYear = e.EndYear
                }).ToList()
            };
        }
        else
        {
            step2 = new Step2ExperienceCoreInputModel
            {
                YearsExperience = defaultYears,
                Seniority = profile?.Seniority ?? CandidateProfileRules.SuggestSeniorityFromYears(defaultYears)
            };
        }

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
            EngineSkills = engineSkills,
            CandidateSkillsForTagging = candidateSkillsForTagging,
            Step1 = step1,
            Step2 = step2,
            Step3 = step3,
            Step4 = step4,
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

    private async Task<CandidateSettingsPageViewModel> BuildSettingsPageViewModelAsync(
        Guid userId,
        CandidateSettingsSnapshot settings,
        string? studioQuery,
        UpdateCandidatePrivacyInputModel? privacyFormOverride,
        CancellationToken cancellationToken)
    {
        var state = await _profileService.GetOnboardingStateAsync(userId, cancellationToken);
        var trimmedQuery = string.IsNullOrWhiteSpace(studioQuery) ? null : studioQuery.Trim();

        IReadOnlyList<BlockableStudioSearchRowViewModel> searchResults = Array.Empty<BlockableStudioSearchRowViewModel>();
        if (!string.IsNullOrWhiteSpace(trimmedQuery) && trimmedQuery.Length >= 2)
        {
            var searchOutcome = await _settingsService.SearchStudiosToBlockAsync(userId, trimmedQuery, cancellationToken);
            if (searchOutcome.IsSuccess)
            {
                searchResults = searchOutcome.Value
                    .Select(s => new BlockableStudioSearchRowViewModel(
                        StudioId: s.StudioId,
                        Name: s.Name,
                        Slug: s.Slug,
                        LogoUrl: ResolveStudioLogoUrlOrNull(s.LogoPath),
                        PublicStudioUrl: _portalNavigator.BuildPortalUrl(PortalSite.Jobs, HttpContext, $"/studios/{s.Slug}"),
                        CityName: s.CityName,
                        StateCode: s.StateCode,
                        StudioType: s.StudioType,
                        IsAlreadyBlocked: s.IsAlreadyBlocked))
                    .ToArray();
            }
            else
            {
                ModelState.AddModelError(string.Empty, searchOutcome.Error.Message);
            }
        }

        var blockedRows = settings.BlockedStudios
            .Select(b => new BlockedStudioRowViewModel(
                StudioId: b.StudioId,
                Name: b.Name,
                Slug: b.Slug,
                LogoUrl: ResolveStudioLogoUrlOrNull(b.LogoPath),
                PublicStudioUrl: _portalNavigator.BuildPortalUrl(PortalSite.Jobs, HttpContext, $"/studios/{b.Slug}"),
                CityName: b.CityName,
                StateCode: b.StateCode,
                StudioType: b.StudioType,
                BlockedAt: b.BlockedAt))
            .ToArray();

        var privacyForm = privacyFormOverride ?? new UpdateCandidatePrivacyInputModel
        {
            IsDiscoverable = settings.IsDiscoverable,
            Availability = settings.Availability,
            CurrentCtcLpa = settings.CurrentCtcLpa,
            ShowCurrentCtc = settings.ShowCurrentCtc
        };

        return new CandidateSettingsPageViewModel
        {
            Settings = settings,
            Completeness = state.CompletenessBreakdown,
            PrivacyForm = privacyForm,
            BlockedStudios = blockedRows,
            StudioSearchQuery = trimmedQuery,
            SearchResults = searchResults
        };
    }

    private string? ResolveStudioLogoUrlOrNull(string? logoPath)
    {
        if (string.IsNullOrWhiteSpace(logoPath))
        {
            return null;
        }

        return ObjectKeyRules.TryNormalize(logoPath, out var normalized, out _)
            ? _fileStorageService.ResolvePublicUrl(normalized)
            : null;
    }

    private static string? FormatStudioLocation(string? cityName, string? stateCode)
    {
        if (string.IsNullOrWhiteSpace(cityName))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(stateCode)
            ? cityName
            : $"{cityName}, {stateCode}";
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
