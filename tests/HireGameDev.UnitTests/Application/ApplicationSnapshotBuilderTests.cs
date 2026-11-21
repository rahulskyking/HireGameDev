using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Entities;
using Xunit;

namespace HireGameDev.UnitTests.Application;

public sealed class ApplicationSnapshotBuilderTests
{
    [Fact]
    public void Snapshot_MasksNdaAndUnsharedCtc_AndDoesNotTrackLaterChanges()
    {
        var studio = new StudioProfile { Id = Guid.NewGuid(), Name = "Studio" };
        var job = new Job { StudioId = studio.Id, Title = "Developer", ScreeningQuestion = "Why?" };
        var game = new ShippedTitle { GameName = "Secret", StudioName = "Hidden", IsNda = true, HideStudioInPublic = true,
            Role = "Engineer", StoreUrl = "https://example.com/secret", Platforms = new[] { "pc" } };
        var candidate = new CandidateProfile { FullName = "Alex", City = new City { Name = "Kolkata" }, Phone = "+919999999999",
            CurrentCtcLpa = 10, ShowCurrentCtc = false, ShippedTitles = new List<ShippedTitle> { game } };
        var snapshot = ApplicationSnapshotBuilder.Create(candidate, job, studio, "Because");
        candidate.FullName = "Changed";
        game.Platforms[0] = "web";
        game.IsNda = false;
        Assert.Equal("Alex", snapshot.Candidate.FullName);
        Assert.Null(snapshot.Candidate.CurrentCtcLpa);
        Assert.Equal("+919999999999", snapshot.Phone);
        var credit = Assert.Single(snapshot.Candidate.ShippedGames);
        Assert.Equal(ApplicationSnapshotBuilder.ConfidentialProject, credit.GameName);
        Assert.Equal(ApplicationSnapshotBuilder.ConfidentialStudio, credit.StudioName);
        Assert.Null(credit.StoreUrl);
        Assert.Equal("pc", Assert.Single(credit.Platforms));
        Assert.Equal("Because", snapshot.Job.ScreeningAnswer);
    }

    [Fact]
    public void Snapshot_CopiesOnlyOptedInCtc_AndFailsClosedForMissingReferences()
    {
        var studio = new StudioProfile { Id = Guid.NewGuid(), Name = "Studio" };
        var job = new Job { StudioId = studio.Id, Title = "Developer" };
        var candidate = new CandidateProfile { City = new City { Name = "Pune" }, CurrentCtcLpa = 12, ShowCurrentCtc = true };
        Assert.Equal(12m, ApplicationSnapshotBuilder.Create(candidate, job, studio, null).Candidate.CurrentCtcLpa);
        Assert.Throws<ArgumentException>(() => ApplicationSnapshotBuilder.Create(candidate, job, studio, "unexpected"));
        candidate.Skills.Add(new CandidateSkill());
        Assert.Throws<InvalidOperationException>(() => ApplicationSnapshotBuilder.Create(candidate, job, studio, null));
        candidate.City = null;
        Assert.Throws<InvalidOperationException>(() => ApplicationSnapshotBuilder.Create(candidate, job, studio, null));
        Assert.Throws<ArgumentException>(() => ApplicationSnapshotBuilder.Create(candidate, job, new StudioProfile { Id = Guid.NewGuid() }, null));
    }
}
