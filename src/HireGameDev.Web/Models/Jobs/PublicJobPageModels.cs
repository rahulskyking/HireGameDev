using System.Globalization;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Application.Features.ReferenceData;
using HireGameDev.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace HireGameDev.Web.Models.Jobs;

public sealed record PublicJobBrowsePage(PublicJobSearch Search, PublicJobSearchPage? Results,
    IReadOnlyList<CityLookupItem> Cities, IReadOnlyList<SkillLookupItem> Skills,
    string CanonicalUrl, string RegistrationUrl);
public sealed record PublicJobDetailPage(PublicJobDetail Job, string CanonicalUrl, string RegistrationUrl);

public static class PublicJobUi
{
    public static string Type(JobType type) => type switch
    { JobType.FullTime => "Full-time", JobType.PartTime => "Part-time", JobType.Contract => "Contract", _ => "Internship" };
    public static string Salary(PublicJobCard job) => $"₹{Number(job.SalaryMin)}–{Number(job.SalaryMax)} " +
        (job.PayUnit == JobPayUnit.Lpa ? "LPA" : "/ month");
    public static string Experience(PublicJobCard job) => Number(job.MinYears) +
        (job.MaxYears.HasValue ? "–" + Number(job.MaxYears.Value) : "+") + " years";
    private static string Number(decimal number) => number.ToString("0.##", CultureInfo.InvariantCulture);
    public static string PageUrl(PublicJobSearch search, int page)
    {
        var values = new List<KeyValuePair<string, string?>>();
        void Add(string name, object? value) { if (value is not null) values.Add(new(name, Convert.ToString(value, CultureInfo.InvariantCulture))); }
        if (!string.IsNullOrWhiteSpace(search.Keyword)) Add("Keyword", search.Keyword.Trim());
        Add("Category", search.Category); Add("JobType", search.JobType); Add("WorkMode", search.WorkMode);
        Add("CityId", search.CityId); Add("ExperienceYears", search.ExperienceYears);
        foreach (var skill in search.SkillIds ?? Array.Empty<int>()) Add("SkillIds", skill);
        if (search.FresherFriendlyOnly) Add("FresherFriendlyOnly", "true");
        if (search.PageSize != 20) Add("PageSize", search.PageSize);
        if (page > 1) Add("Page", page);
        return "/jobs" + QueryString.Create(values);
    }
}
