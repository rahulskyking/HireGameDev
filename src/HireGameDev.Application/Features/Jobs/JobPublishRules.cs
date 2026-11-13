using System.Text.RegularExpressions;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;

namespace HireGameDev.Application.Features.Jobs;

/// <summary>Pure field/content rules. Database eligibility and duplicates are checked separately.</summary>
public static class JobPublishRules
{
    public static JobPublishCheckResult Evaluate(JobContent content, decimal salaryMaxSpread = 1.5m)
    {
        var findings = new List<JobPublishFinding>();
        void Block(string code, string field, string message) => findings.Add(new("Jobs." + code, JobFindingSeverity.BlockingError, field, message));
        void Warn(string code, string field, string message) => findings.Add(new("Jobs." + code, JobFindingSeverity.Warning, field, message));
        if (salaryMaxSpread < 1 || salaryMaxSpread > 10)
        {
            Block("InvalidSalarySpreadSetting", "SalaryMax", "Publishing is unavailable because the salary-spread setting is invalid.");
            return new(findings);
        }
        Text("Title", content.Title, 5, 100);
        Text("Description", content.Description, 100, 8000);
        if (content.ScreeningQuestion?.Length > 300) Block("ScreeningQuestionLength", "ScreeningQuestion", "Keep the screening question within 300 characters.");
        else if (!string.IsNullOrWhiteSpace(content.ScreeningQuestion)) Inspect("ScreeningQuestion", content.ScreeningQuestion, false);
        Choice("Category", content.Category);
        Choice("JobType", content.JobType);
        Choice("SeniorityMin", content.SeniorityMin);
        Choice("SeniorityMax", content.SeniorityMax);
        Choice("WorkMode", content.WorkMode);
        Choice("PayUnit", content.PayUnit);
        if (content.SeniorityMin.HasValue && content.SeniorityMax.HasValue && content.SeniorityMin > content.SeniorityMax)
            Block("SeniorityRange", "SeniorityMax", "Maximum seniority cannot be below minimum seniority.");
        if (!content.MinYears.HasValue || content.MinYears is < 0 or > 99.9m)
            Block("MinYears", "MinYears", "Enter minimum experience between 0 and 99.9 years.");
        if (content.MaxYears is < 0 or > 99.9m || content.MaxYears < content.MinYears)
            Block("ExperienceRange", "MaxYears", "Maximum experience must be within 0–99.9 and not below the minimum.");
        if (!content.SalaryMin.HasValue || content.SalaryMin is <= 0 or > 99999999.99m)
            Block("SalaryMin", "SalaryMin", "Enter a positive minimum salary within the supported range.");
        if (!content.SalaryMax.HasValue || content.SalaryMax is <= 0 or > 99999999.99m || content.SalaryMax < content.SalaryMin)
            Block("SalaryMax", "SalaryMax", "Enter a positive maximum salary no lower than the minimum.");
        if (content.SalaryMin > 0 && content.SalaryMin <= 99999999.99m && content.SalaryMax > content.SalaryMin * salaryMaxSpread)
            Block("SalarySpread", "SalaryMax", $"Narrow the salary range to at most {salaryMaxSpread:0.##} times the minimum.");
        if (content.PayUnit == JobPayUnit.MonthlyInr && content.JobType is not (JobType.Contract or JobType.Internship))
            Block("PayUnitForJobType", "PayUnit", "Use LPA for full-time/part-time jobs; monthly INR is available for contracts and internships.");
        if (content.WorkMode == JobWorkMode.Remote && content.CityId.HasValue)
            Block("RemoteCity", "CityId", "Remove the city for a remote job.");
        if ((content.WorkMode is JobWorkMode.Onsite or JobWorkMode.Hybrid) && !content.CityId.HasValue)
            Block("CityRequired", "CityId", "Choose a city for onsite or hybrid work.");
        if (content.CityId <= 0) Block("CityInvalid", "CityId", "Choose a valid city.");
        if (content.Openings is < 1 or > 50) Block("Openings", "Openings", "Choose between 1 and 50 openings.");
        if (content.MaxNoticeDays.HasValue && content.MaxNoticeDays is not (0 or 15 or 30 or 45 or 60 or 90))
            Block("NoticePeriod", "MaxNoticeDays", "Choose a supported notice period or leave it blank.");
        var skills = content.Skills;
        if (skills is null || skills.Count is < 3 or > 12)
            Block("SkillCount", "Skills", "Choose 3–12 skills.");
        if (skills is not null)
        {
            if (!skills.Any(x => x.IsRequired)) Block("RequiredSkill", "Skills", "Mark at least one skill as required.");
            if (skills.Select(x => x.SkillId).Distinct().Count() != skills.Count || skills.Any(x => x.SkillId <= 0))
                Block("DistinctSkills", "Skills", "Choose distinct valid skills.");
            if (skills.Any(x => !Enum.IsDefined(x.MinProficiency))) Block("SkillProficiency", "Skills", "Choose proficiency from 1 to 4.");
        }
        if (content.FresherFriendly && content.MinYears > 1)
            Warn("FresherExperienceMismatch", "MinYears", "This job is fresher-friendly but asks for more than one year. Consider adjusting one of these fields.");
        if ((content.SeniorityMax == SeniorityLevel.Fresher && content.MinYears >= 1) ||
            ((content.SeniorityMin is SeniorityLevel.Senior or SeniorityLevel.Lead) && content.MinYears < 3))
            Warn("SeniorityExperienceMismatch", "MinYears", "Review whether the experience requirement matches the selected seniority range.");
        if (!string.IsNullOrWhiteSpace(content.Description) && content.Description.Length <= 8000)
        {
            var words = content.Description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 25 || (words.Length >= 25 && words.Distinct(StringComparer.OrdinalIgnoreCase).Count() < words.Length / 4))
                Warn("DescriptionDetail", "Description", "Explain the work, responsibilities and essential requirements with more specific, varied detail.");
        }
        return new(findings);

        void Choice<T>(string field, T? value) where T : struct, Enum
        {
            if (!value.HasValue || !Enum.IsDefined(value.Value)) Block(field + "Required", field, "Select a valid " + field + ".");
        }
        void Text(string field, string? text, int min, int max)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < min || text.Length > max)
            {
                Block(field + "Length", field, $"Enter {min}–{max} characters.");
                return;
            }
            Inspect(field, text, field == "Title");
        }
        void Inspect(string field, string text, bool title)
        {
            try
            {
                var report = JobContentPolicy.Inspect(text);
                if (report.HasEmail || report.HasPhone) Block("ContactDetails", field, "Remove email addresses and phone numbers. Candidates must apply through HireGameDev.");
                if (report.HasLink) Block("ExternalLink", field, "Remove links. Keep applications on HireGameDev and use the Studio profile for company links.");
                if (title && report.HasSalary) Block("SalaryInTitle", field, "Move compensation information into the salary fields.");
                if (title && report.IsAllCaps) Block("AllCapsTitle", field, "Use readable mixed-case wording instead of an ALL CAPS title.");
                if (title && report.HasEmojiSpam) Block("EmojiSpam", field, "Remove excessive emoji from the job title.");
            }
            catch (ArgumentException)
            {
                Block("InvalidText", field, "Use valid Unicode text in this field.");
            }
            catch (RegexMatchTimeoutException)
            {
                Block("ContentCheckTimeout", field, "Content could not be checked safely. Simplify the text and try again.");
            }
        }
    }
}
