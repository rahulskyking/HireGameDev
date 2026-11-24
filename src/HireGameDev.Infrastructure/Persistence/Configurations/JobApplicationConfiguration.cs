using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> b)
    {
        b.ToTable("applications", t =>
        {
            t.HasCheckConstraint("ck_applications_status", "status IN ('submitted','reviewing','shortlisted','rejected','hired','withdrawn')");
            t.HasCheckConstraint("ck_applications_consent_receipt", "(consent_version IS NULL AND consented_at IS NULL) OR (consent_version IS NOT NULL AND consented_at IS NOT NULL AND char_length(btrim(consent_version)) > 0)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion(new SnakeCaseEnumValueConverter<ApplicationStatus>()).HasColumnType("text").IsRequired();
        b.Property(x => x.ConsentVersion).HasMaxLength(80);
        b.Property(x => x.ConsentedAt).HasColumnType("timestamptz");
        b.Property(x => x.SnapshotPurgedAt).HasColumnType("timestamptz");
        b.Property(x => x.SubmittedAt).HasColumnType("timestamptz");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz");
        b.Property(x => x.Version).IsRowVersion();
        b.HasOne<CandidateProfile>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CandidateId, x.JobId }).IsUnique().HasDatabaseName("ux_applications_candidate_job");
        b.HasIndex(x => new { x.CandidateId, x.SubmittedAt, x.Id }).IsDescending(false, true, false).HasDatabaseName("ix_applications_candidate_submitted");
        b.HasIndex(x => new { x.JobId, x.SubmittedAt, x.Id }).IsDescending(false, true, false).HasDatabaseName("ix_applications_job_submitted");
    }
}

internal sealed class ApplicationSnapshotConfiguration : IEntityTypeConfiguration<ApplicationSnapshot>
{
    public void Configure(EntityTypeBuilder<ApplicationSnapshot> b)
    {
        b.ToTable("application_snapshots", t =>
        {
            t.HasCheckConstraint("ck_application_snapshots_version", "snapshot_version > 0");
            t.HasCheckConstraint("ck_application_snapshots_consent", "char_length(btrim(consent_version)) > 0");
            t.HasCheckConstraint("ck_application_snapshots_json", "jsonb_typeof(candidate_data) = 'object' AND jsonb_typeof(job_data) = 'object'");
        });
        b.HasKey(x => x.ApplicationId);
        b.Property(x => x.ApplicationId).ValueGeneratedNever();
        b.Property(x => x.CandidateData).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.JobData).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Phone).HasMaxLength(20);
        b.Property(x => x.ConsentVersion).HasMaxLength(80).IsRequired();
        b.Property(x => x.ConsentedAt).HasColumnType("timestamptz");
        b.HasOne<JobApplication>().WithOne().HasForeignKey<ApplicationSnapshot>(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ApplicationStatusHistoryConfiguration : IEntityTypeConfiguration<ApplicationStatusHistory>
{
    public void Configure(EntityTypeBuilder<ApplicationStatusHistory> b)
    {
        b.ToTable("application_status_history", t =>
        {
            t.HasCheckConstraint("ck_application_history_previous", "previous_status IS NULL OR previous_status IN ('submitted','reviewing','shortlisted','rejected','hired','withdrawn')");
            t.HasCheckConstraint("ck_application_history_new", "new_status IN ('submitted','reviewing','shortlisted','rejected','hired','withdrawn')");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.PreviousStatus).HasConversion(new SnakeCaseEnumValueConverter<ApplicationStatus>()).HasColumnType("text");
        b.Property(x => x.NewStatus).HasConversion(new SnakeCaseEnumValueConverter<ApplicationStatus>()).HasColumnType("text").IsRequired();
        b.Property(x => x.OccurredAt).HasColumnType("timestamptz");
        b.HasOne<JobApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ApplicationId, x.OccurredAt, x.Id }).HasDatabaseName("ix_application_history_timeline");
    }
}
