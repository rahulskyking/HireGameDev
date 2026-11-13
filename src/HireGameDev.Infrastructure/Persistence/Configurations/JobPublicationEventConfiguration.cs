using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class JobPublicationEventConfiguration : IEntityTypeConfiguration<JobPublicationEvent>
{
    public void Configure(EntityTypeBuilder<JobPublicationEvent> b)
    {
        b.ToTable("job_publication_events", t =>
        {
            t.HasCheckConstraint("ck_job_publication_kind", "kind IN ('initial','renewal')");
            t.HasCheckConstraint("ck_job_publication_title", "char_length(btrim(normalized_title)) BETWEEN 1 AND 100");
            t.HasCheckConstraint("ck_job_publication_fingerprint", "content_fingerprint ~ '^[0-9a-f]{64}$'");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasConversion(new SnakeCaseEnumValueConverter<JobPublicationKind>()).HasColumnType("text");
        b.Property(x => x.NormalizedTitle).HasMaxLength(100).IsRequired();
        b.Property(x => x.ContentFingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.PublishedAt).HasColumnType("timestamptz").HasDefaultValueSql("now()");
        b.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StudioProfile>().WithMany().HasForeignKey(x => x.StudioId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StudioId, x.PublishedAt }).IsDescending(false, true).HasDatabaseName("ix_job_publications_studio_time");
        b.HasIndex(x => new { x.StudioId, x.ContentFingerprint, x.PublishedAt }).IsDescending(false, false, true).HasDatabaseName("ix_job_publications_repeat");
        b.HasIndex(x => x.JobId).IsUnique().HasFilter("kind = 'initial'").HasDatabaseName("ux_job_publications_initial");
    }
}
