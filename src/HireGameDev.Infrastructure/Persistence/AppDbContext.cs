using HireGameDev.Application.Abstractions;
using HireGameDev.Domain.Common;
using HireGameDev.Domain.Entities;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence.Configurations;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Persistence;

public sealed class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>, IUnitOfWork
{
    private readonly IClock _clock;

    public AppDbContext(DbContextOptions<AppDbContext> options, IClock clock)
        : base(options)
    {
        _clock = clock;
    }

    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobSkill> JobSkills => Set<JobSkill>();
    public DbSet<JobStatusHistory> JobStatusHistory => Set<JobStatusHistory>();
    public DbSet<JobPublicationEvent> JobPublicationEvents => Set<JobPublicationEvent>();

    public DbSet<City> Cities => Set<City>();

    public DbSet<Skill> Skills => Set<Skill>();

    public DbSet<SkillRelation> SkillRelations => Set<SkillRelation>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();

    public DbSet<CandidateProfile> CandidateProfiles => Set<CandidateProfile>();

    public DbSet<CandidateSkill> CandidateSkills => Set<CandidateSkill>();

    public DbSet<CandidateExperience> CandidateExperiences => Set<CandidateExperience>();

    public DbSet<CandidateEducation> CandidateEducation => Set<CandidateEducation>();

    public DbSet<ShippedTitle> ShippedTitles => Set<ShippedTitle>();

    public DbSet<PortfolioLink> PortfolioLinks => Set<PortfolioLink>();

    public DbSet<PortfolioLinkSkill> PortfolioLinkSkills => Set<PortfolioLinkSkill>();

    public DbSet<ResumeParseLog> ResumeParseLogs => Set<ResumeParseLog>();

    public DbSet<CandidateStudioBlock> CandidateStudioBlocks => Set<CandidateStudioBlock>();

    public DbSet<StudioProfile> StudioProfiles => Set<StudioProfile>();

    public DbSet<StudioMember> StudioMembers => Set<StudioMember>();

    public DbSet<StudioTitle> StudioTitles => Set<StudioTitle>();

    public DbSet<StudioSkill> StudioSkills => Set<StudioSkill>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("citext");
        builder.HasPostgresExtension("pg_trgm");

        builder.ConfigureIdentityTables();
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyAuditTimestamps()
    {
        var utcNow = _clock.UtcNow;

        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = utcNow;
                }

                entry.Entity.UpdatedAt = utcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = utcNow;
            }
        }
    }
}
