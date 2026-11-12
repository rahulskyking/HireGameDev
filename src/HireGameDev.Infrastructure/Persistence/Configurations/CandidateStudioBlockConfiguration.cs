using HireGameDev.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class CandidateStudioBlockConfiguration : IEntityTypeConfiguration<CandidateStudioBlock>
{
    public void Configure(EntityTypeBuilder<CandidateStudioBlock> builder)
    {
        builder.ToTable("candidate_studio_blocks");

        builder.HasKey(b => new { b.CandidateId, b.StudioId });

        builder.Property(b => b.CandidateId)
            .IsRequired();

        builder.Property(b => b.StudioId)
            .IsRequired();

        builder.Property(b => b.CreatedAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne(b => b.Candidate)
            .WithMany()
            .HasForeignKey(b => b.CandidateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.Studio)
            .WithMany()
            .HasForeignKey(b => b.StudioId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(b => b.StudioId)
            .HasDatabaseName("ix_candidate_studio_blocks_studio_id");
    }
}
