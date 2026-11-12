using System.Security.Claims;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Abstractions.Studios;
using HireGameDev.Application.Features.Studios;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Web.Authorization;
using HireGameDev.Web.Models.Studio;
using HireGameDev.Web.Portals;
using HireGameDev.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HireGameDev.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.StudioMember)]
[Route("Studio")]
public sealed class StudioController : Controller
{
    private const string ActiveStudioCookieName = "hgd-active-studio";

    private readonly IStudioProfileService _studioProfileService;
    private readonly IStudioLogoService _studioLogoService;
    private readonly IStudioTeamService _studioTeamService;
    private readonly IStudioVerificationService _studioVerificationService;
    private readonly IReferenceDataService _referenceDataService;
    private readonly IPortalNavigator _portalNavigator;

    public StudioController(
        IStudioProfileService studioProfileService,
        IStudioLogoService studioLogoService,
        IStudioTeamService studioTeamService,
        IStudioVerificationService studioVerificationService,
        IReferenceDataService referenceDataService,
        IPortalNavigator portalNavigator)
    {
        _studioProfileService = studioProfileService;
        _studioLogoService = studioLogoService;
        _studioTeamService = studioTeamService;
        _studioVerificationService = studioVerificationService;
        _referenceDataService = referenceDataService;
        _portalNavigator = portalNavigator;
    }

    [HttpGet("")]
    [HttpGet("Profile")]
    public async Task<IActionResult> Index(Guid? studioId = null, CancellationToken cancellationToken = default)
    {
        if (TryBuildStudioPortalRedirect(out var portalRedirect))
        {
            return Redirect(portalRedirect);
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var memberships = await _studioProfileService.GetUserMembershipsAsync(userId, cancellationToken);
        if (memberships.Count == 0)
        {
            return RedirectToAction(nameof(Onboarding));
        }

        var targetStudioId = ResolveRequestedOrRememberedStudioId(studioId, memberships);
        return RedirectToAction(nameof(EditProfile), new { studioId = targetStudioId });
    }

    [HttpGet("Onboarding")]
    public async Task<IActionResult> Onboarding(CancellationToken cancellationToken = default)
    {
        if (TryBuildStudioPortalRedirect(out var portalRedirect))
        {
            return Redirect(portalRedirect);
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var ownedStudio = await _studioProfileService.GetOwnedActiveStudioSummaryAsync(userId, cancellationToken);
        if (ownedStudio is not null)
        {
            RememberActiveStudioCookie(ownedStudio.StudioId);
            return RedirectToAction(nameof(EditProfile), new { studioId = ownedStudio.StudioId });
        }

        var memberships = await _studioProfileService.GetUserMembershipsAsync(userId, cancellationToken);
        var vm = await BuildWorkspacePageViewModelAsync(
            isOnboarding: true,
            studio: null,
            ownedActiveStudio: null,
            memberships: memberships,
            form: new StudioProfileFormInputModel(),
            cancellationToken: cancellationToken);

        return View("Onboarding", vm);
    }

    [HttpPost("Onboarding")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Onboarding(
        [Bind(Prefix = "Form")] StudioProfileFormInputModel form,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        form ??= new StudioProfileFormInputModel();

        var ownedStudio = await _studioProfileService.GetOwnedActiveStudioSummaryAsync(userId, cancellationToken);
        if (ownedStudio is not null)
        {
            RememberActiveStudioCookie(ownedStudio.StudioId);
            TempData["ErrorMessage"] = "You already own an active studio workspace.";
            return RedirectToAction(nameof(EditProfile), new { studioId = ownedStudio.StudioId });
        }

        var command = new CreateStudioProfileCommand(
            ActorUserId: userId,
            Name: form.Name ?? string.Empty,
            Slug: form.Slug,
            Website: form.Website,
            Description: form.Description ?? string.Empty,
            CityId: form.CityId,
            SizeBand: form.SizeBand,
            StudioType: form.StudioType,
            FoundedYear: form.FoundedYear,
            CultureTags: (IReadOnlyList<string>?)form.CultureTags ?? Array.Empty<string>(),
            Gstin: form.Gstin,
            SkillIds: (IReadOnlyList<int>?)form.SkillIds ?? Array.Empty<int>(),
            Titles: MapTitleInputs(form.Titles));

        var result = await _studioProfileService.CreateStudioAsync(command, cancellationToken);
        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error.Message);
            var memberships = await _studioProfileService.GetUserMembershipsAsync(userId, cancellationToken);
            var vm = await BuildWorkspacePageViewModelAsync(
                isOnboarding: true,
                studio: null,
                ownedActiveStudio: null,
                memberships: memberships,
                form: form,
                cancellationToken: cancellationToken);
            return View("Onboarding", vm);
        }

        RememberActiveStudioCookie(result.Value.Id);
        TempData["StatusMessage"] = $"'{result.Value.Name}' workspace created! Upload your studio logo or preview your studio page.";
        return RedirectToAction(nameof(EditProfile), new { studioId = result.Value.Id });
    }

    [HttpGet("Profile/Edit")]
    public async Task<IActionResult> EditProfile(Guid? studioId = null, CancellationToken cancellationToken = default)
    {
        if (TryBuildStudioPortalRedirect(out var portalRedirect))
        {
            return Redirect(portalRedirect);
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var memberships = await _studioProfileService.GetUserMembershipsAsync(userId, cancellationToken);
        if (memberships.Count == 0)
        {
            return RedirectToAction(nameof(Onboarding));
        }

        var targetStudioId = ResolveRequestedOrRememberedStudioId(studioId, memberships);
        var studioResult = await _studioProfileService.GetStudioForMemberAsync(userId, targetStudioId, cancellationToken);
        if (studioResult.IsFailure)
        {
            if (string.Equals(studioResult.Error.Code, "Studio.NotOnboarded", StringComparison.Ordinal))
            {
                return RedirectToAction(nameof(Onboarding));
            }

            // Fallback to primary membership if a stale studioId query param was supplied.
            studioResult = await _studioProfileService.GetStudioForMemberAsync(userId, null, cancellationToken);
            if (studioResult.IsFailure)
            {
                return RedirectToAction(nameof(Onboarding));
            }
        }

        var studio = studioResult.Value;
        RememberActiveStudioCookie(studio.Id);

        var ownedStudio = memberships.FirstOrDefault(m => m.MemberRole == StudioMemberRole.Owner);
        var vm = await BuildWorkspacePageViewModelAsync(
            isOnboarding: false,
            studio: studio,
            ownedActiveStudio: ownedStudio,
            memberships: memberships,
            form: MapDetailToForm(studio),
            cancellationToken: cancellationToken);

        return View("EditProfile", vm);
    }

    [HttpPost("Profile/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditProfile(
        [Bind(Prefix = "Form")] StudioProfileFormInputModel form,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        form ??= new StudioProfileFormInputModel();

        var memberships = await _studioProfileService.GetUserMembershipsAsync(userId, cancellationToken);
        if (memberships.Count == 0)
        {
            return RedirectToAction(nameof(Onboarding));
        }

        var targetStudioId = ResolveRequestedOrRememberedStudioId(form.StudioId, memberships);
        var currentStudioResult = await _studioProfileService.GetStudioForMemberAsync(userId, targetStudioId, cancellationToken);
        if (currentStudioResult.IsFailure)
        {
            TempData["ErrorMessage"] = currentStudioResult.Error.Message;
            return RedirectToAction(nameof(Index));
        }

        var currentStudio = currentStudioResult.Value;
        var command = new UpdateStudioProfileCommand(
            ActorUserId: userId,
            StudioId: currentStudio.Id,
            Name: form.Name ?? string.Empty,
            Slug: form.Slug,
            Website: form.Website,
            Description: form.Description ?? string.Empty,
            CityId: form.CityId,
            SizeBand: form.SizeBand,
            StudioType: form.StudioType,
            FoundedYear: form.FoundedYear,
            CultureTags: (IReadOnlyList<string>?)form.CultureTags ?? Array.Empty<string>(),
            Gstin: form.Gstin,
            SkillIds: (IReadOnlyList<int>?)form.SkillIds ?? Array.Empty<int>(),
            Titles: MapTitleInputs(form.Titles));

        var updateResult = await _studioProfileService.UpdateStudioProfileAsync(command, cancellationToken);
        if (updateResult.IsFailure)
        {
            ModelState.AddModelError(string.Empty, updateResult.Error.Message);
            form.StudioId = currentStudio.Id;
            if (!currentStudio.CanEditIdentityFields)
            {
                form.Name = currentStudio.Name;
                form.Gstin = currentStudio.Gstin;
            }

            if (!currentStudio.CanEditSlug)
            {
                form.Slug = currentStudio.Slug;
            }

            var ownedStudio = memberships.FirstOrDefault(m => m.MemberRole == StudioMemberRole.Owner);
            var vm = await BuildWorkspacePageViewModelAsync(
                isOnboarding: false,
                studio: currentStudio,
                ownedActiveStudio: ownedStudio,
                memberships: memberships,
                form: form,
                cancellationToken: cancellationToken);
            return View("EditProfile", vm);
        }

        RememberActiveStudioCookie(updateResult.Value.Id);
        TempData["StatusMessage"] = "Studio profile saved.";
        return RedirectToAction(nameof(EditProfile), new { studioId = updateResult.Value.Id });
    }

    [HttpPost("Logo/Upload")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ImageUploadRules.MaxMultipartRequestBytes)]
    public async Task<IActionResult> UploadLogo(
        Guid studioId,
        IFormFile? logo,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        if (studioId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Select a valid studio workspace before uploading a logo.";
            return RedirectToAction(nameof(EditProfile));
        }

        if (logo is null || logo.Length <= 0)
        {
            TempData["ErrorMessage"] = "Please select a PNG, JPEG, or WebP studio logo image up to 1 MB.";
            return RedirectToAction(nameof(EditProfile), new { studioId });
        }

        if (logo.Length > ImageUploadRules.MaxImageUploadBytes)
        {
            TempData["ErrorMessage"] = "Studio logo must be 1 MB or smaller.";
            return RedirectToAction(nameof(EditProfile), new { studioId });
        }

        await using var stream = logo.OpenReadStream();
        var result = await _studioLogoService.UploadLogoAsync(
            new UploadStudioLogoCommand(
                ActorUserId: userId,
                StudioId: studioId,
                Content: stream,
                FileName: logo.FileName,
                DeclaredContentType: logo.ContentType),
            cancellationToken);

        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["StatusMessage"] = "Studio logo updated.";
        }

        return RedirectToAction(nameof(EditProfile), new { studioId });
    }

    [HttpPost("Logo/Remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveLogo(Guid studioId, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        if (studioId == Guid.Empty)
        {
            return RedirectToAction(nameof(EditProfile));
        }

        var result = await _studioLogoService.RemoveLogoAsync(userId, studioId, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["StatusMessage"] = "Studio logo removed.";
        }

        return RedirectToAction(nameof(EditProfile), new { studioId });
    }

    [HttpGet("Profile/Preview")]
    public async Task<IActionResult> Preview(Guid? studioId = null, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var memberships = await _studioProfileService.GetUserMembershipsAsync(userId, cancellationToken);
        if (memberships.Count == 0)
        {
            return RedirectToAction(nameof(Onboarding));
        }

        var targetStudioId = ResolveRequestedOrRememberedStudioId(studioId, memberships);
        var studioResult = await _studioProfileService.GetStudioForMemberAsync(userId, targetStudioId, cancellationToken);
        if (studioResult.IsFailure)
        {
            return RedirectToAction(nameof(EditProfile));
        }

        RememberActiveStudioCookie(studioResult.Value.Id);
        var publicUrl = _portalNavigator.BuildPortalUrl(
            PortalSite.Jobs,
            HttpContext,
            $"/studios/{studioResult.Value.Slug}");

        return Redirect(publicUrl);
    }

    [HttpGet("Slug/Check")]
    public async Task<IActionResult> CheckSlug(
        string? slug,
        Guid? excludeStudioId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _studioProfileService.CheckSlugAvailabilityAsync(
            slug ?? string.Empty,
            excludeStudioId,
            cancellationToken);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error.Message });
        }

        return Json(new
        {
            normalizedSlug = result.Value.NormalizedSlug,
            isAvailable = result.Value.IsAvailable,
            reason = result.Value.Reason
        });
    }

    [HttpGet("Team")]
    public async Task<IActionResult> Team(Guid? studioId = null, CancellationToken cancellationToken = default)
    {
        if (TryBuildStudioPortalRedirect(out var portalRedirect))
        {
            return Redirect(portalRedirect);
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var memberships = await _studioProfileService.GetUserMembershipsAsync(userId, cancellationToken);
        if (memberships.Count == 0)
        {
            return RedirectToAction(nameof(Onboarding));
        }

        var resolvedId = ResolveRequestedOrRememberedStudioId(studioId, memberships) ?? memberships[0].StudioId;
        var teamResult = await _studioTeamService.GetTeamAsync(userId, resolvedId, cancellationToken);
        if (teamResult.IsFailure)
        {
            teamResult = await _studioTeamService.GetTeamAsync(userId, memberships[0].StudioId, cancellationToken);
            if (teamResult.IsFailure)
            {
                TempData["ErrorMessage"] = teamResult.Error.Message;
                return RedirectToAction(nameof(EditProfile));
            }
        }

        RememberActiveStudioCookie(teamResult.Value.StudioId);
        var ownedStudio = memberships.FirstOrDefault(m => m.MemberRole == StudioMemberRole.Owner);

        return View("Team", new StudioTeamPageViewModel
        {
            Team = teamResult.Value,
            OwnedActiveStudio = ownedStudio,
            Memberships = memberships
        });
    }

    [HttpPost("Team/Add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddTeamMember(
        Guid studioId,
        [Bind(Prefix = "AddMember")] AddStudioTeamMemberInputModel input,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        input ??= new AddStudioTeamMemberInputModel();

        var addResult = await _studioTeamService.AddMemberByEmailAsync(
            new AddStudioMemberCommand(
                ActorUserId: userId,
                StudioId: studioId,
                Email: input.Email ?? string.Empty,
                MemberRole: input.MemberRole),
            cancellationToken);

        if (addResult.IsFailure)
        {
            var memberships = await _studioProfileService.GetUserMembershipsAsync(userId, cancellationToken);
            var teamResult = await _studioTeamService.GetTeamAsync(userId, studioId, cancellationToken);
            if (teamResult.IsFailure)
            {
                TempData["ErrorMessage"] = addResult.Error.Message;
                return RedirectToAction(nameof(Team));
            }

            ModelState.AddModelError(string.Empty, addResult.Error.Message);
            return View("Team", new StudioTeamPageViewModel
            {
                Team = teamResult.Value,
                OwnedActiveStudio = memberships.FirstOrDefault(m => m.MemberRole == StudioMemberRole.Owner),
                Memberships = memberships,
                AddMember = input
            });
        }

        TempData["StatusMessage"] = "Teammate added to your studio workspace.";
        return RedirectToAction(nameof(Team), new { studioId });
    }

    [HttpPost("Team/UpdateRole")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTeamMemberRole(
        Guid studioId,
        Guid targetUserId,
        StudioMemberRole newRole,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var result = await _studioTeamService.UpdateMemberRoleAsync(
            new UpdateStudioMemberRoleCommand(
                ActorUserId: userId,
                StudioId: studioId,
                TargetUserId: targetUserId,
                NewRole: newRole),
            cancellationToken);

        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["StatusMessage"] = "Teammate role updated.";
        }

        return RedirectToAction(nameof(Team), new { studioId });
    }

    [HttpPost("Team/Remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveTeamMember(
        Guid studioId,
        Guid targetUserId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var result = await _studioTeamService.RemoveMemberAsync(
            new RemoveStudioMemberCommand(
                ActorUserId: userId,
                StudioId: studioId,
                TargetUserId: targetUserId),
            cancellationToken);

        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["StatusMessage"] = "Teammate removed from studio workspace.";
        }

        return RedirectToAction(nameof(Team), new { studioId });
    }

    [HttpGet("Verification")]
    public async Task<IActionResult> Verification(Guid? studioId = null, CancellationToken cancellationToken = default)
    {
        if (TryBuildStudioPortalRedirect(out var portalRedirect))
        {
            return Redirect(portalRedirect);
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var memberships = await _studioProfileService.GetUserMembershipsAsync(userId, cancellationToken);
        if (memberships.Count == 0)
        {
            return RedirectToAction(nameof(Onboarding));
        }

        var targetStudioId = ResolveRequestedOrRememberedStudioId(studioId, memberships);
        var workspaceResult = await _studioVerificationService.GetVerificationWorkspaceAsync(
            userId,
            targetStudioId,
            cancellationToken);

        if (workspaceResult.IsFailure)
        {
            TempData["ErrorMessage"] = workspaceResult.Error.Message;
            return RedirectToAction(nameof(EditProfile));
        }

        RememberActiveStudioCookie(workspaceResult.Value.StudioId);
        var vm = BuildVerificationPageViewModel(
            workspaceResult.Value,
            memberships,
            submitFormOverride: null,
            identityFormOverride: null,
            expandIdentityForm: false);

        return View("Verification", vm);
    }

    [HttpPost("Verification/Submit")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.StudioVerificationSubmit)]
    public async Task<IActionResult> SubmitVerification(
        [Bind(Prefix = "SubmitForm")] SubmitStudioVerificationInputModel? submitForm,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        submitForm ??= new SubmitStudioVerificationInputModel();
        var memberships = await _studioProfileService.GetUserMembershipsAsync(userId, cancellationToken);
        if (memberships.Count == 0)
        {
            return RedirectToAction(nameof(Onboarding));
        }

        var targetStudioId = ResolveRequestedOrRememberedStudioId(submitForm.StudioId, memberships);
        if (!targetStudioId.HasValue)
        {
            return RedirectToAction(nameof(Onboarding));
        }

        var submitResult = await _studioVerificationService.SubmitForVerificationAsync(
            new SubmitStudioVerificationCommand(
                ActorUserId: userId,
                StudioId: targetStudioId.Value,
                Website: submitForm.Website,
                Gstin: submitForm.Gstin,
                ProofUrls: (IReadOnlyList<string>?)submitForm.ProofUrls ?? Array.Empty<string>(),
                ApplicantNotes: submitForm.ApplicantNotes),
            cancellationToken);

        if (submitResult.IsFailure)
        {
            var currentWorkspace = await _studioVerificationService.GetVerificationWorkspaceAsync(
                userId,
                targetStudioId,
                cancellationToken);

            if (currentWorkspace.IsFailure)
            {
                TempData["ErrorMessage"] = submitResult.Error.Message;
                return RedirectToAction(nameof(Verification));
            }

            ModelState.AddModelError(string.Empty, submitResult.Error.Message);
            var vm = BuildVerificationPageViewModel(
                currentWorkspace.Value,
                memberships,
                submitFormOverride: submitForm,
                identityFormOverride: null,
                expandIdentityForm: false);

            return View("Verification", vm);
        }

        TempData["StatusMessage"] = "Verification request submitted! Our team reviews studio submissions within 24–48 hours.";
        return RedirectToAction(nameof(Verification), new { studioId = submitResult.Value.StudioId });
    }

    [HttpPost("Verification/RequestIdentityChange")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.StudioVerificationSubmit)]
    public async Task<IActionResult> RequestIdentityChange(
        [Bind(Prefix = "IdentityChangeForm")] RequestStudioIdentityChangeInputModel? identityForm,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        identityForm ??= new RequestStudioIdentityChangeInputModel();
        var memberships = await _studioProfileService.GetUserMembershipsAsync(userId, cancellationToken);
        if (memberships.Count == 0)
        {
            return RedirectToAction(nameof(Onboarding));
        }

        var targetStudioId = ResolveRequestedOrRememberedStudioId(identityForm.StudioId, memberships);
        if (!targetStudioId.HasValue)
        {
            return RedirectToAction(nameof(Onboarding));
        }

        var requestResult = await _studioVerificationService.RequestIdentityChangeAsync(
            new RequestStudioIdentityChangeCommand(
                ActorUserId: userId,
                StudioId: targetStudioId.Value,
                ProposedName: identityForm.ProposedName ?? string.Empty,
                ProposedSlug: identityForm.ProposedSlug ?? string.Empty,
                ProposedGstin: identityForm.ProposedGstin,
                ProofUrls: (IReadOnlyList<string>?)identityForm.ProofUrls ?? Array.Empty<string>(),
                ApplicantNotes: identityForm.ApplicantNotes ?? string.Empty),
            cancellationToken);

        if (requestResult.IsFailure)
        {
            var currentWorkspace = await _studioVerificationService.GetVerificationWorkspaceAsync(
                userId,
                targetStudioId,
                cancellationToken);

            if (currentWorkspace.IsFailure)
            {
                TempData["ErrorMessage"] = requestResult.Error.Message;
                return RedirectToAction(nameof(Verification));
            }

            ModelState.AddModelError(string.Empty, requestResult.Error.Message);
            var vm = BuildVerificationPageViewModel(
                currentWorkspace.Value,
                memberships,
                submitFormOverride: null,
                identityFormOverride: identityForm,
                expandIdentityForm: true);

            return View("Verification", vm);
        }

        TempData["StatusMessage"] = "Identity re-verification request submitted. Your live verified studio page and badge stay active under your current details until admin review completes.";
        return RedirectToAction(nameof(Verification), new { studioId = requestResult.Value.StudioId });
    }

    [HttpPost("Verification/CancelIdentityChange")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelIdentityChange(
        Guid studioId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var cancelResult = await _studioVerificationService.CancelIdentityChangeAsync(
            new CancelStudioIdentityChangeCommand(userId, studioId),
            cancellationToken);

        if (cancelResult.IsFailure)
        {
            TempData["ErrorMessage"] = cancelResult.Error.Message;
        }
        else
        {
            TempData["StatusMessage"] = "Pending identity update request cancelled. Your live verified identity remains unchanged.";
        }

        return RedirectToAction(nameof(Verification), new { studioId });
    }

    private static StudioVerificationPageViewModel BuildVerificationPageViewModel(
        StudioVerificationWorkspaceModel workspace,
        IReadOnlyList<StudioMembershipSummary> memberships,
        SubmitStudioVerificationInputModel? submitFormOverride,
        RequestStudioIdentityChangeInputModel? identityFormOverride,
        bool expandIdentityForm)
    {
        var ownedStudio = memberships.FirstOrDefault(m => m.MemberRole == StudioMemberRole.Owner);

        var submitForm = submitFormOverride ?? new SubmitStudioVerificationInputModel
        {
            StudioId = workspace.StudioId,
            Website = workspace.Website,
            Gstin = workspace.Gstin,
            ProofUrls = workspace.VerificationProofUrls.Count > 0
                ? workspace.VerificationProofUrls.ToList()
                : new List<string> { string.Empty },
            ApplicantNotes = workspace.VerificationApplicantNotes
        };

        submitForm.StudioId = workspace.StudioId;
        submitForm.ProofUrls ??= new List<string> { string.Empty };
        if (submitForm.ProofUrls.Count == 0)
        {
            submitForm.ProofUrls.Add(string.Empty);
        }

        var identityForm = identityFormOverride ?? new RequestStudioIdentityChangeInputModel
        {
            StudioId = workspace.StudioId,
            ProposedName = workspace.StudioName,
            ProposedSlug = workspace.StudioSlug,
            ProposedGstin = workspace.Gstin,
            ProofUrls = workspace.VerificationProofUrls.Count > 0
                ? workspace.VerificationProofUrls.ToList()
                : new List<string> { string.Empty },
            ApplicantNotes = string.Empty
        };

        identityForm.StudioId = workspace.StudioId;
        identityForm.ProofUrls ??= new List<string> { string.Empty };
        if (identityForm.ProofUrls.Count == 0)
        {
            identityForm.ProofUrls.Add(string.Empty);
        }

        return new StudioVerificationPageViewModel
        {
            Workspace = workspace,
            OwnedActiveStudio = ownedStudio,
            Memberships = memberships,
            SubmitForm = submitForm,
            IdentityChangeForm = identityForm,
            ExpandIdentityChangeForm = expandIdentityForm
        };
    }

    private async Task<StudioWorkspacePageViewModel> BuildWorkspacePageViewModelAsync(
        bool isOnboarding,
        StudioProfileDetail? studio,
        StudioMembershipSummary? ownedActiveStudio,
        IReadOnlyList<StudioMembershipSummary> memberships,
        StudioProfileFormInputModel form,
        CancellationToken cancellationToken)
    {
        var cities = await _referenceDataService.GetActiveCitiesAsync(cancellationToken);
        var skills = await _referenceDataService.GetActiveSkillsAsync(cancellationToken: cancellationToken);

        var engineSkills = skills
            .Where(s => s.Category == SkillCategory.Engine)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var genreSkills = skills
            .Where(s => s.Category == SkillCategory.Genre)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var platformSkills = skills
            .Where(s => s.Category == SkillCategory.Platform)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        form.CultureTags ??= new List<string>();
        form.SkillIds ??= new List<int>();
        form.Titles ??= new List<StudioTitleRowInputModel>();

        return new StudioWorkspacePageViewModel
        {
            IsOnboarding = isOnboarding,
            Studio = studio,
            OwnedActiveStudio = ownedActiveStudio,
            Memberships = memberships,
            Form = form,
            Cities = cities,
            EngineSkills = engineSkills,
            GenreSkills = genreSkills,
            PlatformSkills = platformSkills
        };
    }

    private static StudioProfileFormInputModel MapDetailToForm(StudioProfileDetail detail)
    {
        return new StudioProfileFormInputModel
        {
            StudioId = detail.Id,
            Name = detail.Name,
            Slug = detail.Slug,
            Website = detail.Website,
            Description = detail.Description ?? string.Empty,
            CityId = detail.CityId ?? 0,
            SizeBand = detail.SizeBand,
            StudioType = detail.StudioType,
            FoundedYear = detail.FoundedYear,
            CultureTags = detail.CultureTags.ToList(),
            Gstin = detail.Gstin,
            SkillIds = detail.Skills.Select(s => s.SkillId).ToList(),
            Titles = detail.Titles
                .Select(t => new StudioTitleRowInputModel
                {
                    Id = t.Id,
                    GameName = t.GameName,
                    ReleaseYear = t.ReleaseYear,
                    Platforms = t.Platforms.ToList(),
                    StoreUrl = t.StoreUrl
                })
                .ToList()
        };
    }

    private static IReadOnlyList<StudioTitleInput> MapTitleInputs(List<StudioTitleRowInputModel>? rows)
    {
        if (rows is null || rows.Count == 0)
        {
            return Array.Empty<StudioTitleInput>();
        }

        var result = new List<StudioTitleInput>(rows.Count);
        foreach (var row in rows)
        {
            if (row is null)
            {
                continue;
            }

            var hasAnyContent =
                !string.IsNullOrWhiteSpace(row.GameName) ||
                row.ReleaseYear.HasValue ||
                !string.IsNullOrWhiteSpace(row.StoreUrl) ||
                (row.Platforms is { Count: > 0 });

            if (!hasAnyContent)
            {
                continue;
            }

            result.Add(new StudioTitleInput(
                Id: row.Id,
                GameName: row.GameName ?? string.Empty,
                ReleaseYear: row.ReleaseYear,
                Platforms: (IReadOnlyList<string>?)row.Platforms ?? Array.Empty<string>(),
                StoreUrl: row.StoreUrl));
        }

        return result;
    }

    private Guid? ResolveRequestedOrRememberedStudioId(
        Guid? explicitStudioId,
        IReadOnlyList<StudioMembershipSummary> memberships)
    {
        if (explicitStudioId.HasValue &&
            explicitStudioId.Value != Guid.Empty &&
            memberships.Any(m => m.StudioId == explicitStudioId.Value))
        {
            return explicitStudioId.Value;
        }

        if (Request.Cookies.TryGetValue(ActiveStudioCookieName, out var rawCookie) &&
            Guid.TryParse(rawCookie, out var cookieStudioId) &&
            memberships.Any(m => m.StudioId == cookieStudioId))
        {
            return cookieStudioId;
        }

        return memberships.Count > 0 ? memberships[0].StudioId : null;
    }

    private void RememberActiveStudioCookie(Guid studioId)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        };

        var sharedDomain = _portalNavigator.ResolveSharedCookieDomain(HttpContext);
        if (!string.IsNullOrWhiteSpace(sharedDomain))
        {
            cookieOptions.Domain = sharedDomain;
        }

        Response.Cookies.Append(ActiveStudioCookieName, studioId.ToString("D"), cookieOptions);
    }

    private bool TryBuildStudioPortalRedirect(out string redirectUrl)
    {
        redirectUrl = string.Empty;
        if (!HttpMethods.IsGet(Request.Method))
        {
            return false;
        }

        var portal = _portalNavigator.ResolvePortal(HttpContext);
        if (portal == PortalSite.Studio || _portalNavigator.DeploymentMode != PortalDeploymentMode.All)
        {
            return false;
        }

        if (portal == PortalSite.Jobs || _portalNavigator.ResolveSharedCookieDomain(HttpContext) is not null)
        {
            var pathAndQuery = $"{Request.Path}{Request.QueryString}";
            redirectUrl = _portalNavigator.BuildPortalUrl(PortalSite.Studio, HttpContext, pathAndQuery);
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
