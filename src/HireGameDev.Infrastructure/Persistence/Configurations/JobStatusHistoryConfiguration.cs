using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class JobStatusHistoryConfiguration : IEntityTypeConfiguration<JobStatusHistory>
{
    public void Configure(EntityTypeBuilder<JobStatusHistory> b)
    {
        b.ToTable("job_status_history", t =>
        {
            t.HasCheckConstraint("ck_job_history_previous", "previous_status IS NULL OR previous_status IN ('draft','active','paused','full','expired','closed','removed')");
            t.HasCheckConstraint("ck_job_history_new", "new_status IN ('draft','active','paused','full','expired','closed','removed')");
            t.HasCheckConstraint("ck_job_history_actor", "(actor_type = 'user' AND actor_user_id IS NOT NULL) OR (actor_type = 'system' AND actor_user_id IS NULL)");
            t.HasCheckConstraint("ck_job_history_reason_code", "char_length(btrim(reason_code)) BETWEEN 1 AND 80");
            t.HasCheckConstraint("ck_job_history_removal", "new_status <> 'removed' OR (actor_type = 'user' AND reason IS NOT NULL AND char_length(btrim(reason)) BETWEEN 1 AND 1000)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.PreviousStatus).HasConversion(new SnakeCaseEnumValueConverter<JobStatus>()).HasColumnType("text");
        b.Property(x => x.NewStatus).HasConversion(new SnakeCaseEnumValueConverter<JobStatus>()).HasColumnType("text");
        b.Property(x => x.ActorType).HasConversion(new SnakeCaseEnumValueConverter<JobHistoryActorType>()).HasColumnType("text");
        b.Property(x => x.ReasonCode).HasMaxLength(80).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.OccurredAt).HasColumnType("timestamptz").HasDefaultValueSql("now()");
        b.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.JobId, x.OccurredAt, x.Id }).HasDatabaseName("ix_job_history_timeline");
    }
}
