using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace HireGameDev.UnitTests.Infrastructure;

public sealed class ApplicationSchemaTests
{
    [Fact]
    public void ApplicationModel_RequiresUniqueCandidateJob_AndRestrictsParents()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=schema_only;Username=schema_only")
            .UseSnakeCaseNamingConvention().Options, new SystemClock());
        var model = db.GetService<IDesignTimeModel>().Model;
        var application = model.FindEntityType(typeof(JobApplication))!;
        Assert.Equal("applications", application.GetTableName());
        Assert.Contains(application.GetIndexes(), i => i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "CandidateId", "JobId" }));
        Assert.All(application.GetForeignKeys(), f => Assert.Equal(DeleteBehavior.Restrict, f.DeleteBehavior));
        Assert.True(application.FindProperty(nameof(JobApplication.Version))!.IsConcurrencyToken);
        Assert.Equal("xmin", application.FindProperty(nameof(JobApplication.Version))!.GetColumnName());
        Assert.Equal("withdrawn", application.FindProperty(nameof(JobApplication.Status))!.GetValueConverter()!.ConvertToProvider(ApplicationStatus.Withdrawn));
        Assert.Contains(application.GetCheckConstraints(), c => c.Name == "ck_applications_status");
        var snapshot = model.FindEntityType(typeof(ApplicationSnapshot))!;
        Assert.Equal("jsonb", snapshot.FindProperty(nameof(ApplicationSnapshot.CandidateData))!.GetColumnType());
        Assert.Equal(DeleteBehavior.Restrict, snapshot.GetForeignKeys().Single().DeleteBehavior);
        Assert.Contains(snapshot.GetCheckConstraints(), c => c.Name == "ck_application_snapshots_json");
        var history = model.FindEntityType(typeof(ApplicationStatusHistory))!;
        Assert.Equal(DeleteBehavior.Restrict, history.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(JobApplication)).DeleteBehavior);
        Assert.Equal(DeleteBehavior.Restrict, history.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(ApplicationUser)).DeleteBehavior);
    }
}
