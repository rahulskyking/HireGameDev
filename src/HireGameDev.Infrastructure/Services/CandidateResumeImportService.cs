using System.Diagnostics;
using FluentValidation;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Audit;
using HireGameDev.Application.Abstractions.Candidates;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Abstractions.Settings;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Audit;
using HireGameDev.Application.Features.Candidates;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HireGameDev.Infrastructure.Services;

/// <summary>
/// Orchestrates the 7-step privacy-first Resume AI Import pipeline (Blueprint v2.0 Section 5):
/// daily quota check -> bounded PDF text extraction -> 200-char minimum guard -> PII redaction ->
/// pluggable <see cref="IResumeLlmClient"/> -> strict C# JSON validation -> taxonomy skill matching ->
/// metadata-only <c>resume_parse_logs</c> recording, followed by candidate-reviewed non-destructive profile merge.
/// </summary>
internal sealed class CandidateResumeImportService : ICandidateResumeImportService
{
    public const string DailyParseLimitSettingKey = "limits.resume_parse_per_day";

    private readonly AppDbContext _dbContext;
    private readonly IResumePdfTextExtractor _pdfTextExtractor;
    private readonly IResumeLlmClient _llmClient;
    private readonly IReferenceDataService _referenceDataService;
    private readonly ISettingsService _settingsService;
    private readonly IValidator<ApplyCandidateResumeDraftCommand> _applyValidator;
    private readonly ICandidateCompletenessCalculator _completenessCalculator;
    private readonly IEvidenceService _evidenceService;
    private readonly IAuditLogger _auditLogger;
    private readonly IClock _clock;
    private readonly ILogger<CandidateResumeImportService> _logger;

    public CandidateResumeImportService(
        AppDbContext dbContext,
        IResumePdfTextExtractor pdfTextExtractor,
        IResumeLlmClient llmClient,
        IReferenceDataService referenceDataService,
        ISettingsService settingsService,
        IValidator<ApplyCandidateResumeDraftCommand> applyValidator,
        ICandidateCompletenessCalculator completenessCalculator,
        IEvidenceService evidenceService,
        IAuditLogger auditLogger,
        IClock clock,
        ILogger<CandidateResumeImportService> logger)
    {
        _dbContext = dbContext;
        _pdfTextExtractor = pdfTextExtractor;
        _llmClient = llmClient;
        _referenceDataService = referenceDataService;
        _settingsService = settingsService;
        _applyValidator = applyValidator;
        _completenessCalculator = completenessCalculator;
        _evidenceService = evidenceService;
        _auditLogger = auditLogger;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ResumeParseQuotaStatus> GetQuotaStatusAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var dailyLimit = Math.Max(
            1,
            await _settingsService.GetIntAsync(
                DailyParseLimitSettingKey,
                ResumePdfUploadRules.DefaultDailyParseLimit,
                cancellationToken));

        var utcDayStart = GetUtcStartOfDay(_clock.UtcNow);

        var usedToday = await _dbContext.ResumeParseLogs
            .AsNoTracking()
            .CountAsync(
                r => r.UserId == userId &&
                     r.CreatedAt >= utcDayStart &&
                     r.Status != ResumeParseStatus.Rejected,
                cancellationToken);

        var remaining = Math.Max(0, dailyLimit - usedToday);
        return new ResumeParseQuotaStatus(dailyLimit, usedToday, remaining);
    }

    public async Task<Result<ResumeImportDraft>> ParseResumePdfAsync(
        ParseCandidateResumeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.UserId == Guid.Empty)
        {
            return Result.Failure<ResumeImportDraft>(
                Error.Unauthorized("ResumeImport.Unauthorized", "Sign in as a candidate to import a resume."));
        }

        // 1. Enforce daily per-user parse quota (limits.resume_parse_per_day = 5) + anti-flood guard on rejected uploads.
        var quotaBefore = await GetQuotaStatusAsync(command.UserId, cancellationToken);
        if (!quotaBefore.HasQuotaRemaining)
        {
            return Result.Failure<ResumeImportDraft>(
                Error.Validation(
                    "ResumeImport.DailyQuotaExceeded",
                    $"You have reached the daily limit of {quotaBefore.DailyLimit} resume imports for today (UTC). Please try again tomorrow or edit your profile directly."));
        }

        var utcDayStart = GetUtcStartOfDay(_clock.UtcNow);
        var totalAttemptsToday = await _dbContext.ResumeParseLogs
            .AsNoTracking()
            .CountAsync(r => r.UserId == command.UserId && r.CreatedAt >= utcDayStart, cancellationToken);

        if (totalAttemptsToday >= quotaBefore.DailyLimit * 3)
        {
            return Result.Failure<ResumeImportDraft>(
                Error.Validation(
                    "ResumeImport.TooManyRejectedUploads",
                    "Too many unreadable PDF upload attempts today. Please verify your PDF file and try again tomorrow."));
        }

        var stopwatch = Stopwatch.StartNew();

        // 2. Bounded in-memory PDF extraction (<= 2 MB, %PDF- magic header, PdfPig layout text).
        var extractResult = await _pdfTextExtractor.ExtractTextAsync(
            command.PdfStream,
            command.FileName,
            cancellationToken);

        if (extractResult.IsFailure)
        {
            return Result.Failure<ResumeImportDraft>(extractResult.Error);
        }

        var extracted = extractResult.Value;
        if (extracted.NonWhitespaceCharCount < ResumePdfUploadRules.MinExtractedTextChars)
        {
            stopwatch.Stop();
            await RecordParseLogAsync(
                userId: command.UserId,
                source: extracted.DetectedSource,
                status: ResumeParseStatus.Rejected,
                inputChars: extracted.NonWhitespaceCharCount,
                durationMs: (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds),
                cancellationToken: cancellationToken);

            return Result.Failure<ResumeImportDraft>(
                Error.Validation(
                    "ResumeImport.ScannedOrTooShort",
                    "We couldn't extract enough readable text from this PDF (fewer than 200 characters). Scanned image PDFs aren't supported — please upload a text-based PDF or LinkedIn's 'Save to PDF' export."));
        }

        // 3. Redact PII (emails, URLs/handles, phone numbers) BEFORE invoking IResumeLlmClient.
        var redaction = ResumePiiRedactor.RedactWithReport(extracted.ExtractedText);
        extracted = extracted with { ExtractedText = string.Empty };

        var redactedCharCount = redaction.RedactedText.Length;

        // 4. Structured extraction via pluggable IResumeLlmClient (never log redacted or raw text!).
        var llmResult = await _llmClient.ExtractResumeJsonAsync(redaction.RedactedText, cancellationToken);
        if (llmResult.IsFailure)
        {
            stopwatch.Stop();
            await RecordParseLogAsync(
                userId: command.UserId,
                source: extracted.DetectedSource,
                status: ResumeParseStatus.Failed,
                inputChars: redactedCharCount,
                durationMs: (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds),
                cancellationToken: cancellationToken);

            _logger.LogWarning(
                "Resume LLM extraction failed for UserId {UserId} via provider {Provider} (Code: {ErrorCode}).",
                command.UserId,
                _llmClient.ProviderName,
                llmResult.Error.Code);

            return Result.Failure<ResumeImportDraft>(
                Error.Validation(
                    "ResumeImport.ExtractionFailed",
                    "We couldn't automatically structure this resume right now. Please try again or fill in your profile sections directly."));
        }

        // 5. Strict C# JSON validation & bounds sanitization.
        if (!ResumeExtractionJsonParser.TryParseAndSanitize(
                llmResult.Value,
                _clock.UtcNow.Year,
                out var sanitized,
                out var jsonError) ||
            sanitized is null)
        {
            stopwatch.Stop();
            await RecordParseLogAsync(
                userId: command.UserId,
                source: extracted.DetectedSource,
                status: ResumeParseStatus.Failed,
                inputChars: redactedCharCount,
                durationMs: (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds),
                cancellationToken: cancellationToken);

            _logger.LogWarning(
                "Resume JSON schema validation failed for UserId {UserId} via provider {Provider}: {ValidationReason}",
                command.UserId,
                _llmClient.ProviderName,
                jsonError);

            return Result.Failure<ResumeImportDraft>(
                Error.Validation(
                    "ResumeImport.InvalidExtractionOutput",
                    "Resume extraction returned an unexpected structure. Please try again or enter your details manually."));
        }

        // 6. Match extracted skills against active taxonomy (name, aliases, pg_trgm similarity); keep unmatched as suggestions.
        var activeSkills = await _referenceDataService.GetActiveSkillsAsync(cancellationToken: cancellationToken);
        var activeSkillById = activeSkills.ToDictionary(s => s.Id);
        var catalogEntries = activeSkills
            .Select(s => new ResumeCatalogSkillEntry(s.Id, s.Name, s.Slug, s.Category, s.Aliases))
            .ToArray();
        var engineCatalogEntries = catalogEntries
            .Where(s => s.Category == SkillCategory.Engine)
            .ToArray();

        var todayDate = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var currentYear = (short)Math.Clamp(_clock.UtcNow.Year, CandidateProfileRules.MinSkillLastUsedYear, CandidateProfileRules.MaxSkillLastUsedYear);

        // Compute total YearsExperience from merged non-overlapping work roles when not explicitly stated in text.
        var resolvedYearsExperience = sanitized.YearsExperience;
        if (!resolvedYearsExperience.HasValue && sanitized.Experiences.Count > 0)
        {
            var nonInternRoles = sanitized.Experiences
                .Where(e => !e.Title.Contains("intern", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var sourceRoles = nonInternRoles.Count > 0 ? nonInternRoles : sanitized.Experiences.ToList();
            var roleIntervals = sourceRoles
                .Select(e => (e.StartDate, End: e.IsCurrent ? todayDate : (e.EndDate ?? todayDate)))
                .ToArray();
            resolvedYearsExperience = ResumeSignalEnricher.ComputeMergedExperienceYears(roleIntervals);
        }

        // Enrich Shipped Titles with matched EngineSkillId, normalized platforms, and date-correlated studio/role suggestions.
        var enrichedShippedTitles = new List<SanitizedResumeShippedTitleExtraction>(sanitized.ShippedTitles.Count);
        foreach (var rawTitle in sanitized.ShippedTitles)
        {
            int? matchedEngineId = null;
            if (!string.IsNullOrWhiteSpace(rawTitle.EngineName))
            {
                matchedEngineId = ResumeSkillTaxonomyMatcher.MatchSingle(rawTitle.EngineName, engineCatalogEntries)?.SkillId;
            }

            if (!matchedEngineId.HasValue)
            {
                var titleContext = $"{rawTitle.GameName} {rawTitle.Role}";
                foreach (var eng in engineCatalogEntries)
                {
                    if (SkillEvidenceTextMatcher.ContainsAnySkillTerm(titleContext, eng.Name, eng.Aliases))
                    {
                        matchedEngineId = eng.SkillId;
                        break;
                    }
                }
            }

            var suggestedStudio = rawTitle.StudioName;
            var suggestedRole = rawTitle.Role;
            if ((string.IsNullOrWhiteSpace(suggestedStudio) || string.IsNullOrWhiteSpace(suggestedRole)) &&
                sanitized.Experiences.Count > 0)
            {
                SanitizedResumeExperienceExtraction? correlatedExp = null;
                if (rawTitle.ReleaseYear.HasValue)
                {
                    var ry = rawTitle.ReleaseYear.Value;
                    correlatedExp = sanitized.Experiences.FirstOrDefault(e =>
                        e.StartDate.Year <= ry &&
                        ((e.IsCurrent && currentYear >= ry) || (e.EndDate.HasValue && e.EndDate.Value.Year >= ry)));
                }
                else if (sanitized.Experiences.Count == 1)
                {
                    correlatedExp = sanitized.Experiences[0];
                }

                if (correlatedExp is not null)
                {
                    if (string.IsNullOrWhiteSpace(suggestedStudio))
                    {
                        suggestedStudio = correlatedExp.CompanyName;
                    }

                    if (string.IsNullOrWhiteSpace(suggestedRole))
                    {
                        suggestedRole = correlatedExp.Title;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(suggestedRole) &&
                !string.IsNullOrWhiteSpace(sanitized.Headline) &&
                sanitized.Headline.Length <= 120)
            {
                suggestedRole = sanitized.Headline;
            }

            enrichedShippedTitles.Add(rawTitle with
            {
                StudioName = suggestedStudio,
                Role = suggestedRole,
                Platforms = ResumeSignalEnricher.NormalizeGamePlatforms(rawTitle.Platforms),
                MatchedEngineSkillId = matchedEngineId
            });
        }

        var matchedById = new Dictionary<int, (ResumeDraftMatchedSkill DraftSkill, bool LlmMarkedPrimary)>();
        var unmatchedSuggestions = new List<string>();
        var seenUnmatched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawSkill in sanitized.Skills)
        {
            var match = ResumeSkillTaxonomyMatcher.MatchSingle(rawSkill.Name, catalogEntries);
            if (match is not null)
            {
                var suggestedProficiency = SuggestProficiencyFromYears(rawSkill.YearsUsed);
                if (matchedById.TryGetValue(match.SkillId, out var existing))
                {
                    var mergedYears = MaxNullable(existing.DraftSkill.YearsUsed, rawSkill.YearsUsed);
                    var mergedLastYear = MaxNullable(existing.DraftSkill.LastUsedYear, rawSkill.LastUsedYear);
                    matchedById[match.SkillId] = (
                        existing.DraftSkill with
                        {
                            YearsUsed = mergedYears,
                            LastUsedYear = mergedLastYear,
                            SuggestedProficiency = SuggestProficiencyFromYears(mergedYears)
                        },
                        existing.LlmMarkedPrimary || rawSkill.IsPrimary);
                }
                else if (matchedById.Count < CandidateProfileRules.MaxCandidateSkills)
                {
                    matchedById[match.SkillId] = (
                        new ResumeDraftMatchedSkill(
                            SkillId: match.SkillId,
                            SkillName: match.SkillName,
                            SkillSlug: match.SkillSlug,
                            Category: match.Category,
                            MatchKind: match.MatchKind,
                            SuggestedProficiency: suggestedProficiency,
                            YearsUsed: rawSkill.YearsUsed,
                            LastUsedYear: rawSkill.LastUsedYear,
                            RawExtractedName: rawSkill.Name),
                        rawSkill.IsPrimary);
                }
            }
            else if (seenUnmatched.Add(rawSkill.Name))
            {
                unmatchedSuggestions.Add(rawSkill.Name);
            }
        }

        // Infer per-skill YearsUsed, LastUsedYear, Proficiency, and top-5 Primary relevance from dated roles & shipped titles!
        var scoredSkills = new List<(ResumeDraftMatchedSkill Skill, int PrimaryScore)>(matchedById.Count);
        foreach (var (skillId, entry) in matchedById)
        {
            var draftSkill = entry.DraftSkill;
            IReadOnlyList<string> aliases = activeSkillById.TryGetValue(skillId, out var fullSkill)
                ? fullSkill.Aliases
                : [];

            var matchingIntervals = new List<(DateOnly Start, DateOnly End)>();
            short? latestEvidenceYear = null;

            foreach (var exp in sanitized.Experiences)
            {
                var expText = $"{exp.Title} {exp.Description}";
                if (SkillEvidenceTextMatcher.ContainsAnySkillTerm(expText, draftSkill.SkillName, aliases))
                {
                    var expEnd = exp.IsCurrent ? todayDate : (exp.EndDate ?? exp.StartDate);
                    matchingIntervals.Add((exp.StartDate, expEnd));
                    var roleLastYear = (short)Math.Clamp(
                        exp.IsCurrent ? currentYear : expEnd.Year,
                        CandidateProfileRules.MinSkillLastUsedYear,
                        currentYear);
                    latestEvidenceYear = MaxNullable(latestEvidenceYear, roleLastYear);
                }
            }

            foreach (var st in enrichedShippedTitles)
            {
                var isEngineMatch = st.MatchedEngineSkillId == skillId;
                var titleText = $"{st.GameName} {st.Role} {st.EngineName}";
                if (isEngineMatch || SkillEvidenceTextMatcher.ContainsAnySkillTerm(titleText, draftSkill.SkillName, aliases))
                {
                    if (st.ReleaseYear.HasValue)
                    {
                        latestEvidenceYear = MaxNullable(latestEvidenceYear, st.ReleaseYear.Value);
                    }
                }
            }

            var inferredYears = draftSkill.YearsUsed ?? ResumeSignalEnricher.ComputeMergedExperienceYears(matchingIntervals);
            var inferredLastYear = draftSkill.LastUsedYear ?? latestEvidenceYear;
            var updatedProficiency = SuggestProficiencyFromYears(inferredYears);

            var primaryScore = 0;
            if (!string.IsNullOrWhiteSpace(sanitized.Headline) &&
                SkillEvidenceTextMatcher.ContainsAnySkillTerm(sanitized.Headline, draftSkill.SkillName, aliases))
            {
                primaryScore += 60;
            }

            if (entry.LlmMarkedPrimary)
            {
                primaryScore += 35;
            }

            if (draftSkill.Category is SkillCategory.Engine or SkillCategory.Language)
            {
                primaryScore += 15;
            }
            else if (draftSkill.Category is SkillCategory.ArtTool or SkillCategory.DesignAudioTool)
            {
                primaryScore += 10;
            }

            if (matchingIntervals.Count > 0 || latestEvidenceYear.HasValue)
            {
                primaryScore += 20;
            }

            if (inferredYears.HasValue)
            {
                primaryScore += (int)Math.Min(30m, inferredYears.Value * 3m);
            }

            scoredSkills.Add((
                draftSkill with
                {
                    YearsUsed = inferredYears,
                    LastUsedYear = inferredLastYear,
                    SuggestedProficiency = updatedProficiency
                },
                primaryScore));
        }

        var topPrimaryIds = scoredSkills
            .OrderByDescending(x => x.PrimaryScore)
            .ThenBy(x => x.Skill.Category)
            .ThenBy(x => x.Skill.SkillName, StringComparer.OrdinalIgnoreCase)
            .Take(CandidateProfileRules.MaxPrimarySkills)
            .Select(x => x.Skill.SkillId)
            .ToHashSet();

        var orderedMatchedSkills = scoredSkills
            .Select(x => x.Skill with { SuggestedPrimary = topPrimaryIds.Contains(x.Skill.SkillId) })
            .OrderByDescending(s => s.SuggestedPrimary)
            .ThenBy(s => s.Category)
            .ThenBy(s => s.SkillName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Infer primary discipline & Indian city ID for first-time profiles.
        var suggestedDiscipline = sanitized.PrimaryDiscipline ??
            ResumeSignalEnricher.InferPrimaryDiscipline(
                sanitized.Headline,
                sanitized.Experiences.Select(e => e.Title).ToArray(),
                orderedMatchedSkills.Select(s => s.Category).ToArray());

        int? suggestedCityId = null;
        if (!string.IsNullOrWhiteSpace(sanitized.City))
        {
            var activeCities = await _referenceDataService.GetActiveCitiesAsync(cancellationToken);
            var cityCatalog = activeCities
                .Select(c => new ResumeCityCatalogEntry(c.Id, c.Name, c.Slug, c.State, c.StateCode, c.Aliases, c.IsHub))
                .ToArray();
            suggestedCityId = ResumeSignalEnricher.MatchIndianCityId(sanitized.City, null, cityCatalog);
        }

        stopwatch.Stop();
        var elapsedMs = (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds);

        // 7. Record metadata-only log row + audit event (NO resume text ever written).
        _dbContext.ResumeParseLogs.Add(new ResumeParseLog
        {
            UserId = command.UserId,
            Source = extracted.DetectedSource,
            Status = ResumeParseStatus.Ok,
            InputChars = redactedCharCount,
            DurationMs = elapsedMs,
            CreatedAt = _clock.UtcNow
        });

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "candidate.resume.parsed",
            EntityType: "resume_parse_log",
            EntityId: command.UserId.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: command.UserId,
            AfterData: new
            {
                Source = extracted.DetectedSource.ToString(),
                Provider = _llmClient.ProviderName,
                InputChars = redactedCharCount,
                DurationMs = elapsedMs,
                PiiRedactions = redaction.TotalRedactions,
                MatchedSkillCount = orderedMatchedSkills.Length,
                UnmatchedSkillCount = unmatchedSuggestions.Count,
                ExperienceCount = sanitized.Experiences.Count,
                EducationCount = sanitized.Education.Count,
                ShippedTitleCount = enrichedShippedTitles.Count
            }));

        await _dbContext.SaveChangesAsync(cancellationToken);

        var quotaAfter = new ResumeParseQuotaStatus(
            DailyLimit: quotaBefore.DailyLimit,
            UsedToday: quotaBefore.UsedToday + 1,
            RemainingToday: Math.Max(0, quotaBefore.DailyLimit - (quotaBefore.UsedToday + 1)));

        return Result.Success(new ResumeImportDraft(
            Source: extracted.DetectedSource,
            ProviderName: _llmClient.ProviderName,
            Headline: sanitized.Headline,
            YearsExperience: resolvedYearsExperience,
            MatchedSkills: orderedMatchedSkills,
            UnmatchedSkillSuggestions: unmatchedSuggestions,
            Experiences: sanitized.Experiences,
            Education: sanitized.Education,
            ShippedTitles: enrichedShippedTitles,
            QuotaAfterParse: quotaAfter,
            SuggestedFullName: sanitized.FullName,
            Bio: sanitized.Bio,
            SuggestedDiscipline: suggestedDiscipline,
            SuggestedCityId: suggestedCityId));
    }

    public async Task<Result<ApplyCandidateResumeResult>> ApplyImportedDraftAsync(
        ApplyCandidateResumeDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = await _applyValidator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<ApplyCandidateResumeResult>(
                Error.Validation("ResumeImport.InvalidDraft", validation.Errors[0].ErrorMessage));
        }

        var profile = await _dbContext.CandidateProfiles
            .FirstOrDefaultAsync(
                c => c.UserId == command.UserId && c.DeletedAt == null,
                cancellationToken);

        if (profile is null)
        {
            return Result.Failure<ApplyCandidateResumeResult>(
                Error.Validation(
                    "CandidateProfile.Step1Required",
                    "Complete Step 1 (Basics: Full Name, Discipline & City) before applying imported resume sections."));
        }

        // Validate referenced skill IDs and shipped-title engine skill IDs against active catalog.
        var requestedSkillIds = command.Skills.Select(s => s.SkillId).Distinct().ToArray();
        var requestedEngineIds = command.ShippedTitles
            .Where(t => t.EngineSkillId.HasValue)
            .Select(t => t.EngineSkillId!.Value)
            .Distinct()
            .ToArray();

        var allSkillIds = requestedSkillIds.Concat(requestedEngineIds).Distinct().ToArray();
        var activeSkillsById = allSkillIds.Length == 0
            ? new Dictionary<int, Skill>()
            : await _dbContext.Skills
                .AsNoTracking()
                .Where(s => allSkillIds.Contains(s.Id) && s.IsActive)
                .ToDictionaryAsync(s => s.Id, cancellationToken);

        if (requestedSkillIds.Any(id => !activeSkillsById.ContainsKey(id)))
        {
            return Result.Failure<ApplyCandidateResumeResult>(
                Error.Validation("ResumeImport.InvalidSkill", "One or more selected skills are inactive or invalid."));
        }

        if (requestedEngineIds.Any(id =>
                !activeSkillsById.TryGetValue(id, out var eng) || eng.Category != SkillCategory.Engine))
        {
            return Result.Failure<ApplyCandidateResumeResult>(
                Error.Validation("ResumeImport.InvalidEngineSkill", "One or more selected game engines are invalid."));
        }

        if (command.ApplyHeadline && !string.IsNullOrWhiteSpace(command.Headline))
        {
            profile.Headline = command.Headline.Trim();
        }

        if (command.ApplyBio && !string.IsNullOrWhiteSpace(command.Bio))
        {
            profile.Bio = command.Bio.Trim();
        }

        if (command.ApplyYearsExperience && command.YearsExperience.HasValue)
        {
            var roundedYears = decimal.Round(command.YearsExperience.Value, 1, MidpointRounding.AwayFromZero);
            profile.YearsExperience = roundedYears;

            if (!CandidateProfileRules.IsSeniorityWithinAllowedBand(roundedYears, profile.Seniority))
            {
                profile.Seniority = CandidateProfileRules.SuggestSeniorityFromYears(roundedYears);
            }
        }

        // 1. Merge Skills non-destructively (preserving existing proficiency & verified/backed evidence).
        var existingSkills = await _dbContext.CandidateSkills
            .Where(cs => cs.CandidateId == profile.Id)
            .ToListAsync(cancellationToken);

        var existingSkillById = existingSkills.ToDictionary(cs => cs.SkillId);
        var currentSkillTotal = existingSkills.Count;
        var currentPrimaryCount = existingSkills.Count(cs => cs.IsPrimary);
        var addedSkills = 0;
        var updatedSkills = 0;

        foreach (var input in command.Skills.DistinctBy(s => s.SkillId))
        {
            var normalizedYears = input.YearsUsed.HasValue
                ? decimal.Round(input.YearsUsed.Value, 1, MidpointRounding.AwayFromZero)
                : (decimal?)null;

            if (existingSkillById.TryGetValue(input.SkillId, out var existingSkill))
            {
                var changed = false;
                if (!existingSkill.YearsUsed.HasValue && normalizedYears.HasValue)
                {
                    existingSkill.YearsUsed = normalizedYears;
                    changed = true;
                }

                if (!existingSkill.LastUsedYear.HasValue && input.LastUsedYear.HasValue)
                {
                    existingSkill.LastUsedYear = input.LastUsedYear;
                    changed = true;
                }

                if (!existingSkill.IsPrimary && input.IsPrimary && currentPrimaryCount < CandidateProfileRules.MaxPrimarySkills)
                {
                    existingSkill.IsPrimary = true;
                    currentPrimaryCount++;
                    changed = true;
                }

                if (changed)
                {
                    updatedSkills++;
                }
            }
            else if (currentSkillTotal < CandidateProfileRules.MaxCandidateSkills)
            {
                var grantPrimary = input.IsPrimary && currentPrimaryCount < CandidateProfileRules.MaxPrimarySkills;
                if (grantPrimary)
                {
                    currentPrimaryCount++;
                }

                _dbContext.CandidateSkills.Add(new CandidateSkill
                {
                    CandidateId = profile.Id,
                    SkillId = input.SkillId,
                    Proficiency = input.Proficiency,
                    YearsUsed = normalizedYears,
                    LastUsedYear = input.LastUsedYear,
                    EvidenceLevel = SkillEvidenceLevel.Claimed,
                    IsPrimary = grantPrimary
                });

                currentSkillTotal++;
                addedSkills++;
            }
        }

        // 2. Merge Work Experiences non-destructively (deduplicating by CompanyName + Title + StartDate).
        var existingExperiences = await _dbContext.CandidateExperiences
            .Where(e => e.CandidateId == profile.Id)
            .ToListAsync(cancellationToken);

        var expTotal = existingExperiences.Count;
        var addedExperiences = 0;

        foreach (var exp in command.Experiences)
        {
            if (expTotal >= CandidateProfileRules.MaxCandidateExperiences)
            {
                break;
            }

            var company = exp.CompanyName.Trim();
            var title = exp.Title.Trim();
            var isDuplicate = existingExperiences.Any(e =>
                string.Equals(e.CompanyName, company, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Title, title, StringComparison.OrdinalIgnoreCase) &&
                e.StartDate == exp.StartDate);

            if (isDuplicate)
            {
                continue;
            }

            var newExp = new CandidateExperience
            {
                Id = Guid.NewGuid(),
                CandidateId = profile.Id,
                CompanyName = company,
                Title = title,
                StartDate = exp.StartDate,
                EndDate = exp.IsCurrent ? null : exp.EndDate,
                IsCurrent = exp.IsCurrent,
                Description = string.IsNullOrWhiteSpace(exp.Description) ? null : exp.Description.Trim()
            };

            _dbContext.CandidateExperiences.Add(newExp);
            existingExperiences.Add(newExp);
            expTotal++;
            addedExperiences++;
        }

        // 3. Merge Education non-destructively (deduplicating by Institution + Degree).
        var existingEducation = await _dbContext.CandidateEducation
            .Where(e => e.CandidateId == profile.Id)
            .ToListAsync(cancellationToken);

        var eduTotal = existingEducation.Count;
        var addedEducation = 0;

        foreach (var edu in command.Education)
        {
            if (eduTotal >= CandidateProfileRules.MaxCandidateEducationEntries)
            {
                break;
            }

            var institution = edu.Institution.Trim();
            var degree = edu.Degree.Trim();
            var isDuplicate = existingEducation.Any(e =>
                string.Equals(e.Institution, institution, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Degree, degree, StringComparison.OrdinalIgnoreCase));

            if (isDuplicate)
            {
                continue;
            }

            var newEdu = new CandidateEducation
            {
                Id = Guid.NewGuid(),
                CandidateId = profile.Id,
                Institution = institution,
                Degree = degree,
                Field = string.IsNullOrWhiteSpace(edu.Field) ? null : edu.Field.Trim(),
                StartYear = edu.StartYear,
                EndYear = edu.EndYear
            };

            _dbContext.CandidateEducation.Add(newEdu);
            existingEducation.Add(newEdu);
            eduTotal++;
            addedEducation++;
        }

        // 4. Merge Shipped Titles non-destructively (always Verification = None, deduplicating by GameName + StudioName).
        var existingTitles = await _dbContext.ShippedTitles
            .Where(st => st.CandidateId == profile.Id)
            .ToListAsync(cancellationToken);

        var titleTotal = existingTitles.Count;
        var nextSortOrder = existingTitles.Count == 0
            ? (short)0
            : (short)(existingTitles.Max(t => t.SortOrder) + 1);
        var addedTitles = 0;

        foreach (var titleInput in command.ShippedTitles)
        {
            if (titleTotal >= CandidateProfileRules.MaxShippedTitles)
            {
                break;
            }

            var gameName = titleInput.GameName.Trim();
            var studioName = titleInput.StudioName.Trim();
            var isDuplicate = existingTitles.Any(t =>
                string.Equals(t.GameName, gameName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.StudioName, studioName, StringComparison.OrdinalIgnoreCase));

            if (isDuplicate)
            {
                continue;
            }

            var normalizedPlatforms = titleInput.Platforms
                .Select(p => p.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var newTitle = new ShippedTitle
            {
                Id = Guid.NewGuid(),
                CandidateId = profile.Id,
                GameName = gameName,
                StudioName = studioName,
                Role = titleInput.Role.Trim(),
                Platforms = normalizedPlatforms,
                EngineSkillId = titleInput.EngineSkillId,
                ReleaseYear = titleInput.ReleaseYear,
                StoreUrl = null,
                Verification = ShippedTitleVerification.None,
                VerifiedAt = null,
                IsNda = false,
                HideStudioInPublic = false,
                SortOrder = nextSortOrder++
            };

            _dbContext.ShippedTitles.Add(newTitle);
            existingTitles.Add(newTitle);
            titleTotal++;
            addedTitles++;
        }

        // Advance onboarding step conservatively based on populated sections.
        if (titleTotal > 0)
        {
            profile.OnboardingStep = Math.Max(profile.OnboardingStep, (short)5);
        }
        else if (currentSkillTotal >= CandidateProfileRules.MinOnboardingSkills)
        {
            profile.OnboardingStep = Math.Max(profile.OnboardingStep, (short)4);
        }
        else if (expTotal > 0 || eduTotal > 0)
        {
            profile.OnboardingStep = Math.Max(profile.OnboardingStep, (short)3);
        }

        profile.LastActiveAt = _clock.UtcNow;

        // Recompute skill evidence (using pending ChangeTracker entries!) and profile completeness atomically.
        await _evidenceService.RecomputeAndApplyAsync(profile.Id, cancellationToken);

        await _completenessCalculator.RecalculateAndApplyAsync(
            profile,
            new CandidateCompletenessCountOverrides(
                SkillCount: currentSkillTotal,
                ExperienceCount: expTotal,
                EducationCount: eduTotal,
                ShippedTitleCount: titleTotal),
            cancellationToken);

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "candidate.resume.imported",
            EntityType: "candidate_profile",
            EntityId: profile.Id.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: command.UserId,
            AfterData: new
            {
                AddedSkillCount = addedSkills,
                UpdatedSkillCount = updatedSkills,
                AddedExperienceCount = addedExperiences,
                AddedEducationCount = addedEducation,
                AddedShippedTitleCount = addedTitles,
                profile.Completeness,
                profile.OnboardingStep
            }));

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new ApplyCandidateResumeResult(
            AddedSkillCount: addedSkills,
            UpdatedSkillCount: updatedSkills,
            AddedExperienceCount: addedExperiences,
            AddedEducationCount: addedEducation,
            AddedShippedTitleCount: addedTitles,
            UpdatedCompleteness: profile.Completeness));
    }

    private async Task RecordParseLogAsync(
        Guid userId,
        ResumeParseSource source,
        ResumeParseStatus status,
        int inputChars,
        int durationMs,
        CancellationToken cancellationToken)
    {
        _dbContext.ResumeParseLogs.Add(new ResumeParseLog
        {
            UserId = userId,
            Source = source,
            Status = status,
            InputChars = Math.Max(0, inputChars),
            DurationMs = Math.Max(0, durationMs),
            CreatedAt = _clock.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static DateTimeOffset GetUtcStartOfDay(DateTimeOffset timestamp)
    {
        var utc = timestamp.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }

    private static SkillProficiency SuggestProficiencyFromYears(decimal? yearsUsed)
    {
        if (!yearsUsed.HasValue)
        {
            return SkillProficiency.Working;
        }

        return yearsUsed.Value switch
        {
            >= 5.0m => SkillProficiency.Expert,
            >= 2.5m => SkillProficiency.Strong,
            >= 1.0m => SkillProficiency.Working,
            _ => SkillProficiency.Beginner
        };
    }

    private static T? MaxNullable<T>(T? first, T? second)
        where T : struct, IComparable<T>
    {
        if (!first.HasValue)
        {
            return second;
        }

        if (!second.HasValue)
        {
            return first;
        }

        return first.Value.CompareTo(second.Value) >= 0 ? first : second;
    }
}
