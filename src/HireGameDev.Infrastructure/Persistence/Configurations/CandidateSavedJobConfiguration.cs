using HireGameDev.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class CandidateSavedJobConfiguration : IEntityTypeConfiguration<CandidateSavedJob>
{
    public void Configure(EntityTypeBuilder<CandidateSavedJob> builder)
    {
        builder.ToTable("candidate_saved_jobs");
        builder.HasKey(x => new { x.CandidateId, x.JobId });
        builder.Property(x => x.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.HasOne<CandidateProfile>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.JobId).HasDatabaseName("ix_candidate_saved_jobs_job_id");
        builder.HasIndex(x => new { x.CandidateId, x.CreatedAt, x.JobId })
            .IsDescending(false, true, false).HasDatabaseName("ix_candidate_saved_jobs_candidate_created_job");
    }
}
