using System.Text.Json;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Storage;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HireGameDev.UnitTests.Services;

public sealed class PublicJobQueryServiceTests
{
    [Theory]
    [InlineData(JobStatus.Draft)]
    [InlineData(JobStatus.Active)]
    [InlineData(JobStatus.Paused)]
    [InlineData(JobStatus.Full)]
    [InlineData(JobStatus.Expired)]
    [InlineData(JobStatus.Closed)]
    [InlineData(JobStatus.Removed)]
    public async Task ListAndDetail_UseSamePublicStatusGate(JobStatus status)
    {
        await using var h = await Harness.Create();
        var job = await h.Add(status: status);
        var list = await h.Service.SearchAsync(new());
        var detail = await h.Service.GetBySlugAsync(job.Slug);
        var visible = JobLifecycleRules.CanAppearPublicly(job, h.Studio, h.Clock.UtcNow);
        Assert.Equal(visible ? 1 : 0, list.Value.TotalCount);
        Assert.Equal(visible, detail.IsSuccess);
        if (!visible) Assert.Equal("PublicJobs.Unavailable", detail.Error.Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ExpiryBoundary_IsEnforcedWithoutScheduler(int seconds)
    {
        await using var h = await Harness.Create();
        var job = await h.Add();
        job.ExpiresAt = h.Clock.UtcNow.AddSeconds(seconds);
        await h.Db.SaveChangesAsync();
        Assert.Equal(seconds > 0 ? 1 : 0, (await h.Service.SearchAsync(new())).Value.TotalCount);
        Assert.Equal(seconds > 0, (await h.Service.GetBySlugAsync(job.Slug)).IsSuccess);
        Assert.Equal(JobStatus.Active, (await h.Db.Jobs.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(StudioVerificationStatus.Unverified, false, false)]
    [InlineData(StudioVerificationStatus.Pending, false, false)]
    [InlineData(StudioVerificationStatus.Rejected, false, false)]
    [InlineData(StudioVerificationStatus.Verified, true, false)]
    [InlineData(StudioVerificationStatus.Verified, false, true)]
    public async Task StudioRestrictions_HideSearchAndDirectUrls(StudioVerificationStatus status, bool suspended, bool deleted)
    {
        await using var h = await Harness.Create();
        var job = await h.Add();
        h.Studio.VerificationStatus = status;
        h.Studio.SuspendedAt = suspended ? h.Clock.UtcNow : null;
        h.Studio.DeletedAt = deleted ? h.Clock.UtcNow : null;
        await h.Db.SaveChangesAsync();
        Assert.Empty((await h.Service.SearchAsync(new())).Value.Items);
        var missing = await h.Service.GetBySlugAsync("missing-job");
        Assert.Equal(missing.Error, (await h.Service.GetBySlugAsync(job.Slug)).Error);
    }

    [Fact]
    public async Task Filters_CombineWithAnd_SkillsRequireAll_ExperienceIncludesBoundaries()
    {
        await using var h = await Harness.Create();
        var match = await h.Add("Unity Gameplay Engineer");
        match.Description = "Build rendering systems and tools for games.";
        match.CityId = 1; match.WorkMode = JobWorkMode.Hybrid; match.FresherFriendly = true;
        await h.Add("Audio Designer", category: DisciplineCategory.Audio);
        h.Db.JobSkills.AddRange(new JobSkill { JobId = match.Id, SkillId = 1, IsRequired = true }, new JobSkill { JobId = match.Id, SkillId = 2, IsRequired = false });
        await h.Db.SaveChangesAsync();
        var query = new PublicJobSearch { Keyword = "UNITY rendering", Category = DisciplineCategory.Programming,
            JobType = JobType.FullTime, WorkMode = JobWorkMode.Hybrid, CityId = 1, ExperienceYears = 1,
            SkillIds = new[] { 1, 2, 2 }, FresherFriendlyOnly = true };
        Assert.Equal(match.Id, Assert.Single((await h.Service.SearchAsync(query)).Value.Items).Id);
        Assert.Single((await h.Service.SearchAsync(query with { ExperienceYears = 4 })).Value.Items);
        Assert.Empty((await h.Service.SearchAsync(query with { ExperienceYears = 4.1m })).Value.Items);
        Assert.Empty((await h.Service.SearchAsync(query with { SkillIds = new[] { 1, 3 } })).Value.Items);
        Assert.Empty((await h.Service.SearchAsync(query with { Keyword = "UNITY missing" })).Value.Items);
        Assert.Empty((await h.Service.SearchAsync(query with { WorkMode = JobWorkMode.Remote })).Value.Items);
        match.MaxYears = null; await h.Db.SaveChangesAsync();
        Assert.Single((await h.Service.SearchAsync(query with { ExperienceYears = 80 })).Value.Items);
    }

    [Fact]
    public async Task KeywordSymbols_AreLiteral_AndSearchIsCaseInsensitive()
    {
        await using var h = await Harness.Create();
        await h.Add("C++ 100% Gameplay_Engineer");
        await h.Add("Other developer");
        Assert.Single((await h.Service.SearchAsync(new() { Keyword = "%" })).Value.Items);
        Assert.Single((await h.Service.SearchAsync(new() { Keyword = "_engineer" })).Value.Items);
        Assert.Single((await h.Service.SearchAsync(new() { Keyword = "c++" })).Value.Items);
        Assert.Empty((await h.Service.SearchAsync(new() { Keyword = "missing" })).Value.Items);
    }

    [Fact]
    public async Task Pagination_IsBoundedAndStableByPublicationDateThenId_NotUpdatedAt()
    {
        await using var h = await Harness.Create();
        var old = await h.Add("Old publication");
        old.PublishedAt = h.Clock.UtcNow.AddDays(-3); old.FirstPublishedAt = old.PublishedAt;
        await h.Db.SaveChangesAsync();
        var first = await h.Add("Tie one", id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var second = await h.Add("Tie two", id: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        h.Clock.UtcNow = h.Clock.UtcNow.AddHours(1);
        old.Title = "Recently edited old publication"; await h.Db.SaveChangesAsync();
        var query = new PublicJobSearch { PageSize = 1 };
        Assert.Equal(first.Id, Assert.Single((await h.Service.SearchAsync(query)).Value.Items).Id);
        Assert.Equal(second.Id, Assert.Single((await h.Service.SearchAsync(query with { Page = 2 })).Value.Items).Id);
        var last = (await h.Service.SearchAsync(query with { Page = int.MaxValue })).Value;
        Assert.Equal(old.Id, Assert.Single(last.Items).Id);
        Assert.Equal(3, last.Page); Assert.Equal(3, last.TotalCount); Assert.Equal(3, last.TotalPages);
        Assert.Equal(1, (await h.Service.SearchAsync(query with { Page = -1 })).Value.Page);
    }

    [Fact]
    public async Task Detail_ExposesAllowlistedContentAndStableIds_NotPrivateMetadata()
    {
        await using var h = await Harness.Create();
        var job = await h.Add();
        job.ScreeningQuestion = "What have you built?"; job.WorkMode = JobWorkMode.Onsite; job.CityId = 1;
        h.Studio.LogoPath = "logos/test.webp";
        h.Db.JobSkills.Add(new JobSkill { JobId = job.Id, SkillId = 1, IsRequired = true, MinProficiency = SkillProficiency.Strong });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var result = await h.Service.GetBySlugAsync(" " + job.Slug.ToUpperInvariant() + " ");
        Assert.True(result.IsSuccess);
        Assert.Equal(job.Id, result.Value.Summary.Id);
        Assert.Equal(h.Studio.Id, result.Value.Summary.Studio.Id);
        Assert.Equal("/public/logos/test.webp", result.Value.Summary.Studio.LogoUrl);
        Assert.Equal("Pune", result.Value.Summary.CityName);
        Assert.Equal("What have you built?", result.Value.ScreeningQuestion);
        Assert.Equal(SkillProficiency.Strong, Assert.Single(result.Value.Skills).MinProficiency);
        var json = JsonSerializer.Serialize(result.Value);
        foreach (var name in new[] { "PostedByUserId", "ApplicationsCount", "Version", "ContentFingerprint", "VerificationNotes", "SuspensionReason", "OwnerUserId" })
            Assert.DoesNotContain(name, json);
        Assert.Empty(h.Db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task InvalidAndUnboundedInputs_ReturnErrors_NotUnboundedQueries()
    {
        await using var h = await Harness.Create();
        foreach (var query in new PublicJobSearch[] { new() { Keyword = new string('x', 101) }, new() { Keyword = "a b c d e f g h i" },
            new() { Category = (DisciplineCategory)99 }, new() { JobType = (JobType)99 }, new() { WorkMode = (JobWorkMode)99 },
            new() { CityId = 0 }, new() { ExperienceYears = -1 }, new() { ExperienceYears = 100 }, new() { PageSize = 51 },
            new() { PageSize = 0 }, new() { SkillIds = new[] { 0 } }, new() { SkillIds = Enumerable.Range(1, 13).ToArray() }, new() { SkillIds = null! } })
            Assert.Equal("PublicJobs.InvalidSearch", (await h.Service.SearchAsync(query)).Error.Code);
        foreach (var slug in new[] { "", "../draft", "job?secret", "--", new string('x', 161) })
            Assert.Equal("PublicJobs.Unavailable", (await h.Service.GetBySlugAsync(slug)).Error.Code);
        var empty = (await h.Service.SearchAsync(new() { Page = int.MaxValue })).Value;
        Assert.Empty(empty.Items); Assert.Equal(1, empty.Page); Assert.Equal(1, empty.TotalPages);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public TestClock Clock { get; } = new();
        public AppDbContext Db { get; }
        public StudioProfile Studio { get; } = new() { Id = Guid.NewGuid(), Name = "Test Studio", Slug = "test-studio", VerificationStatus = StudioVerificationStatus.Verified };
        public PublicJobQueryService Service { get; }
        private Harness()
        {
            Db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Clock);
            Service = new(Db, Clock, new TestStorage());
        }
        public static async Task<Harness> Create()
        {
            var h = new Harness(); h.Db.StudioProfiles.Add(h.Studio);
            h.Db.Cities.Add(new City { Id = 1, Name = "Pune", StateCode = "MH", Slug = "pune" });
            for (var i = 1; i <= 3; i++) h.Db.Skills.Add(new Skill { Id = i, Name = $"Skill {i}", Slug = $"skill-{i}" });
            await h.Db.SaveChangesAsync(); return h;
        }
        public async Task<Job> Add(string title = "Unity Developer", JobStatus status = JobStatus.Active, DisciplineCategory category = DisciplineCategory.Programming, Guid? id = null)
        {
            var job = new Job { Id = id ?? Guid.NewGuid(), StudioId = Studio.Id, Title = title, Description = "Build gameplay systems.",
                Category = category, Status = status, JobType = JobType.FullTime, WorkMode = JobWorkMode.Remote,
                SeniorityMin = SeniorityLevel.Junior, SeniorityMax = SeniorityLevel.Mid, SalaryMin = 10, SalaryMax = 15,
                PayUnit = JobPayUnit.Lpa, MinYears = 1, MaxYears = 4, PublishedAt = Clock.UtcNow.AddDays(-1),
                FirstPublishedAt = Clock.UtcNow.AddDays(-1), ExpiresAt = Clock.UtcNow.AddDays(29) };
            job.Slug = $"job-{job.Id:N}"; Db.Jobs.Add(job); await Db.SaveChangesAsync(); return job;
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class TestClock : IClock { public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero); }
    private sealed class TestStorage : IFileStorageService
    {
        public string ProviderName => "Test";
        public string ResolvePublicUrl(string key) => "/public/" + key;
        public Task PutAsync(FileStoragePutRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoredObjectReadResult?> OpenReadAsync(string key, StorageVisibility visibility, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteIfExistsAsync(string key, StorageVisibility visibility, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
