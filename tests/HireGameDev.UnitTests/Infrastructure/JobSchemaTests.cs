using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace HireGameDev.UnitTests.Infrastructure;

public sealed class JobSchemaTests
{
    private static AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=schema_only;Username=schema_only")
            .UseSnakeCaseNamingConvention().Options,
        new SystemClock());

    [Fact]
    public void EmptyDraft_DoesNotFabricateMandatoryContent()
    {
        var draft = new Job();
        Assert.Equal(JobStatus.Draft, draft.Status);
        Assert.Null(draft.Title);
        Assert.Null(draft.Category);
        Assert.Null(draft.SalaryMin);
        Assert.Null(draft.PublishedAt);
        Assert.Equal((short)50, draft.ApplicationCap);
        Assert.Equal(SkillProficiency.Working, new JobSkill().MinProficiency);
    }

    [Fact]
    public void NpgsqlModel_MapsNullableDraftsAndXminConcurrency()
    {
        using var db = CreateContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var job = model.FindEntityType(typeof(Job))!;
        Assert.Equal("jobs", job.GetTableName());
        Assert.True(job.FindProperty(nameof(Job.Title))!.IsNullable);
        Assert.True(job.FindProperty(nameof(Job.Category))!.IsNullable);
        Assert.True(job.FindProperty(nameof(Job.Version))!.IsConcurrencyToken);
        Assert.Equal("xmin", job.FindProperty(nameof(Job.Version))!.GetColumnName());
        Assert.Contains(job.GetIndexes(), i => i.IsUnique && i.GetDatabaseName() == "ux_jobs_slug");
        Assert.Contains(job.GetCheckConstraints(), c => c.Name == "ck_jobs_ready");
        Assert.Contains(job.GetCheckConstraints(), c => c.Name == "ck_jobs_removed");
        Assert.Equal("active", job.FindProperty(nameof(Job.Status))!.GetValueConverter()!
            .ConvertToProvider(JobStatus.Active));
        Assert.Equal(new[] { "Draft", "Active", "Paused", "Full", "Expired", "Closed", "Removed" },
            Enum.GetNames<JobStatus>());
    }

    [Fact]
    public void PublicationLedger_RestrictsJobDeletion_AndInitialEventIsUnique()
    {
        using var db = CreateContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var publication = model.FindEntityType(typeof(JobPublicationEvent))!;
        Assert.Equal(DeleteBehavior.Restrict,
            publication.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(Job)).DeleteBehavior);
        Assert.Contains(publication.GetIndexes(), i => i.IsUnique && i.GetFilter() == "kind = 'initial'");
        var skills = model.FindEntityType(typeof(JobSkill))!;
        Assert.Equal(new[] { nameof(JobSkill.JobId), nameof(JobSkill.SkillId) },
            skills.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());
        var studio = model.FindEntityType(typeof(StudioProfile))!;
        Assert.Contains(studio.GetCheckConstraints(), c => c.Name == "ck_studio_profiles_suspension");
    }
}
