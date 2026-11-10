using System.Net.Mail;
using HireGameDev.Application.Common;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;

namespace HireGameDev.Application.Features.Studios;

public sealed record ValidatedStudioTitleData(
    Guid? Id,
    string GameName,
    short? ReleaseYear,
    string[] Platforms,
    string? StoreUrl);

public sealed record ValidatedStudioProfileData(
    string Name,
    string Slug,
    string? Website,
    string? CompanyEmailDomain,
    string Description,
    int CityId,
    StudioSizeBand SizeBand,
    StudioType StudioType,
    short? FoundedYear,
    string[] CultureTags,
    string? Gstin,
    int[] DistinctSkillIds,
    IReadOnlyList<ValidatedStudioTitleData> Titles);

public static class StudioValidators
{
    public static Result<ValidatedStudioProfileData> ValidateProfilePayload(
        Guid actorUserId,
        string? rawName,
        string? rawSlug,
        string? rawWebsite,
        string? rawDescription,
        int cityId,
        StudioSizeBand sizeBand,
        StudioType studioType,
        short? foundedYear,
        IReadOnlyList<string>? rawCultureTags,
        string? rawGstin,
        IReadOnlyList<int>? rawSkillIds,
        IReadOnlyList<StudioTitleInput>? rawTitles,
        int currentUtcYear)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result.Failure<ValidatedStudioProfileData>(
                Error.Validation("StudioProfile.InvalidUser", "A valid user identity is required."));
        }

        var trimmedName = rawName?.Trim() ?? string.Empty;
        if (trimmedName.Length < StudioProfileRules.MinStudioNameLength ||
            trimmedName.Length > StudioProfileRules.MaxStudioNameLength)
        {
            return Result.Failure<ValidatedStudioProfileData>(
                Error.Validation(
                    "StudioProfile.InvalidName",
                    $"Studio name must be between {StudioProfileRules.MinStudioNameLength} and {StudioProfileRules.MaxStudioNameLength} characters."));
        }

        var candidateSlug = string.IsNullOrWhiteSpace(rawSlug)
            ? StudioProfileRules.GenerateSlugFromName(trimmedName)
            : rawSlug;

        if (!StudioProfileRules.TryNormalizeSlug(candidateSlug, out var normalizedSlug, out var slugError))
        {
            return Result.Failure<ValidatedStudioProfileData>(
                Error.Validation("StudioProfile.InvalidSlug", slugError!));
        }

        string? normalizedWebsite = null;
        if (!string.IsNullOrWhiteSpace(rawWebsite))
        {
            if (!ExternalUrlRules.TryNormalizeHttpsUrl(rawWebsite, out var canonicalWebsite, out var websiteError))
            {
                return Result.Failure<ValidatedStudioProfileData>(
                    Error.Validation("StudioProfile.InvalidWebsite", $"Studio website: {websiteError}"));
            }

            normalizedWebsite = canonicalWebsite;
        }

        var trimmedDescription = rawDescription?.Trim() ?? string.Empty;
        if (trimmedDescription.Length < StudioProfileRules.MinOnboardingDescriptionLength ||
            trimmedDescription.Length > StudioProfileRules.MaxDescriptionLength)
        {
            return Result.Failure<ValidatedStudioProfileData>(
                Error.Validation(
                    "StudioProfile.InvalidDescription",
                    $"Studio description must be between {StudioProfileRules.MinOnboardingDescriptionLength} and {StudioProfileRules.MaxDescriptionLength} characters."));
        }

        if (cityId <= 0)
        {
            return Result.Failure<ValidatedStudioProfileData>(
                Error.Validation("StudioProfile.InvalidCity", "Select your studio's primary HQ city."));
        }

        if (!Enum.IsDefined(sizeBand))
        {
            return Result.Failure<ValidatedStudioProfileData>(
                Error.Validation("StudioProfile.InvalidSizeBand", "Select a valid studio team size band."));
        }

        if (!Enum.IsDefined(studioType))
        {
            return Result.Failure<ValidatedStudioProfileData>(
                Error.Validation("StudioProfile.InvalidStudioType", "Select a valid studio type."));
        }

        var maxAllowedFoundedYear = (short)Math.Min(StudioProfileRules.MaxFoundedYear, currentUtcYear);
        if (foundedYear.HasValue &&
            (foundedYear.Value < StudioProfileRules.MinFoundedYear || foundedYear.Value > maxAllowedFoundedYear))
        {
            return Result.Failure<ValidatedStudioProfileData>(
                Error.Validation(
                    "StudioProfile.InvalidFoundedYear",
                    $"Founded year must be between {StudioProfileRules.MinFoundedYear} and {maxAllowedFoundedYear}."));
        }

        if (!StudioProfileRules.TryNormalizeCultureTags(rawCultureTags, out var normalizedCultureTags, out var cultureError))
        {
            return Result.Failure<ValidatedStudioProfileData>(
                Error.Validation("StudioProfile.InvalidCultureTags", cultureError!));
        }

        if (!StudioProfileRules.TryNormalizeGstin(rawGstin, out var normalizedGstin, out var gstinError))
        {
            return Result.Failure<ValidatedStudioProfileData>(
                Error.Validation("StudioProfile.InvalidGstin", gstinError!));
        }

        var distinctSkillIds = rawSkillIds is null
            ? Array.Empty<int>()
            : rawSkillIds.Where(id => id > 0).Distinct().ToArray();

        if (distinctSkillIds.Length < StudioProfileRules.MinRequiredEngineSkills)
        {
            return Result.Failure<ValidatedStudioProfileData>(
                Error.Validation("StudioProfile.EngineRequired", "Select at least 1 game engine your studio works with."));
        }

        if (distinctSkillIds.Length > StudioProfileRules.MaxStudioSkills)
        {
            return Result.Failure<ValidatedStudioProfileData>(
                Error.Validation(
                    "StudioProfile.TooManySkills",
                    $"Select up to {StudioProfileRules.MaxStudioSkills} engines, genres, and platforms."));
        }

        var incomingTitles = rawTitles ?? Array.Empty<StudioTitleInput>();
        if (incomingTitles.Count > StudioProfileRules.MaxStudioTitles)
        {
            return Result.Failure<ValidatedStudioProfileData>(
                Error.Validation(
                    "StudioProfile.TooManyTitles",
                    $"You can list up to {StudioProfileRules.MaxStudioTitles} shipped or announced titles."));
        }

        var maxTitleYear = (short)Math.Min(StudioProfileRules.MaxTitleReleaseYear, currentUtcYear + 3);
        var validatedTitles = new List<ValidatedStudioTitleData>(incomingTitles.Count);

        for (var i = 0; i < incomingTitles.Count; i++)
        {
            var title = incomingTitles[i];
            var trimmedGameName = title.GameName?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(trimmedGameName) &&
                !title.ReleaseYear.HasValue &&
                (title.Platforms is null || title.Platforms.Count == 0) &&
                string.IsNullOrWhiteSpace(title.StoreUrl))
            {
                continue;
            }

            if (trimmedGameName.Length < StudioProfileRules.MinTitleGameNameLength ||
                trimmedGameName.Length > StudioProfileRules.MaxTitleGameNameLength)
            {
                return Result.Failure<ValidatedStudioProfileData>(
                    Error.Validation(
                        "StudioProfile.InvalidTitleName",
                        $"Title #{i + 1}: game name must be between {StudioProfileRules.MinTitleGameNameLength} and {StudioProfileRules.MaxTitleGameNameLength} characters."));
            }

            if (title.ReleaseYear.HasValue &&
                (title.ReleaseYear.Value < StudioProfileRules.MinTitleReleaseYear ||
                 title.ReleaseYear.Value > maxTitleYear))
            {
                return Result.Failure<ValidatedStudioProfileData>(
                    Error.Validation(
                        "StudioProfile.InvalidTitleReleaseYear",
                        $"Title '{trimmedGameName}': release year must be between {StudioProfileRules.MinTitleReleaseYear} and {maxTitleYear}."));
            }

            var rawPlatforms = title.Platforms ?? Array.Empty<string>();
            var normalizedPlatforms = new List<string>(5);
            var seenPlatforms = new HashSet<string>(StringComparer.Ordinal);

            for (var p = 0; p < rawPlatforms.Count; p++)
            {
                if (string.IsNullOrWhiteSpace(rawPlatforms[p]))
                {
                    continue;
                }

                var candidatePlatform = rawPlatforms[p].Trim().ToLowerInvariant();
                if (!CandidateProfileRules.IsValidGamePlatform(candidatePlatform))
                {
                    return Result.Failure<ValidatedStudioProfileData>(
                        Error.Validation(
                            "StudioProfile.InvalidTitlePlatform",
                            $"Title '{trimmedGameName}': unsupported platform '{rawPlatforms[p].Trim()}'."));
                }

                if (seenPlatforms.Add(candidatePlatform))
                {
                    normalizedPlatforms.Add(candidatePlatform);
                }
            }

            string? normalizedStoreUrl = null;
            if (!string.IsNullOrWhiteSpace(title.StoreUrl))
            {
                if (!ExternalUrlRules.TryNormalizeHttpsUrl(title.StoreUrl, out var canonicalStoreUrl, out var storeErr))
                {
                    return Result.Failure<ValidatedStudioProfileData>(
                        Error.Validation(
                            "StudioProfile.InvalidTitleStoreUrl",
                            $"Title '{trimmedGameName}' store URL: {storeErr}"));
                }

                normalizedStoreUrl = canonicalStoreUrl;
            }

            validatedTitles.Add(new ValidatedStudioTitleData(
                Id: title.Id,
                GameName: trimmedGameName,
                ReleaseYear: title.ReleaseYear,
                Platforms: normalizedPlatforms.ToArray(),
                StoreUrl: normalizedStoreUrl));
        }

        var companyDomain = StudioProfileRules.ExtractCompanyDomainFromWebsite(normalizedWebsite);

        return Result.Success(new ValidatedStudioProfileData(
            Name: trimmedName,
            Slug: normalizedSlug,
            Website: normalizedWebsite,
            CompanyEmailDomain: companyDomain,
            Description: trimmedDescription,
            CityId: cityId,
            SizeBand: sizeBand,
            StudioType: studioType,
            FoundedYear: foundedYear,
            CultureTags: normalizedCultureTags,
            Gstin: normalizedGstin,
            DistinctSkillIds: distinctSkillIds,
            Titles: validatedTitles));
    }

    public static Result<string> ValidateAddMemberCommand(AddStudioMemberCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ActorUserId == Guid.Empty || command.StudioId == Guid.Empty)
        {
            return Result.Failure<string>(
                Error.Validation("StudioTeam.InvalidIdentity", "Valid studio and user identifiers are required."));
        }

        if (command.MemberRole is not (StudioMemberRole.Recruiter or StudioMemberRole.Viewer))
        {
            return Result.Failure<string>(
                Error.Validation(
                    "StudioTeam.InvalidRole",
                    "Teammates can be added with the Recruiter or Viewer role only."));
        }

        if (string.IsNullOrWhiteSpace(command.Email))
        {
            return Result.Failure<string>(
                Error.Validation("StudioTeam.EmailRequired", "Enter your teammate's account email address."));
        }

        var trimmedEmail = command.Email.Trim().ToLowerInvariant();
        if (trimmedEmail.Length > 254 || !MailAddress.TryCreate(trimmedEmail, out var parsed) ||
            !string.Equals(parsed.Address, trimmedEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<string>(
                Error.Validation("StudioTeam.InvalidEmail", "Enter a valid email address."));
        }

        return Result.Success(trimmedEmail);
    }

    public static Result ValidateUpdateMemberRoleCommand(UpdateStudioMemberRoleCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ActorUserId == Guid.Empty ||
            command.StudioId == Guid.Empty ||
            command.TargetUserId == Guid.Empty)
        {
            return Result.Failure(
                Error.Validation("StudioTeam.InvalidIdentity", "Valid studio and member identifiers are required."));
        }

        if (command.NewRole is not (StudioMemberRole.Recruiter or StudioMemberRole.Viewer))
        {
            return Result.Failure(
                Error.Validation(
                    "StudioTeam.InvalidRole",
                    "Member role can only be updated to Recruiter or Viewer."));
        }

        return Result.Success();
    }
}
