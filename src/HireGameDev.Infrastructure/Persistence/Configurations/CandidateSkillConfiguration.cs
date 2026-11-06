using HireGameDev.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class CandidateSkillConfiguration : IEntityTypeConfiguration<CandidateSkill>
{
    public void Configure(EntityTypeBuilder<CandidateSkill> builder)
    {
        builder.ToTable("candidate_skills", t =>
        {
            t.HasCheckConstraint(
                "ck_candidate_skills_proficiency",
                "proficiency BETWEEN 1 AND 4");

            t.HasCheckConstraint(
                "ck_candidate_skills_years_used",
                "years_used IS NULL OR (years_used >= 0 AND years_used <= 50)");

            t.HasCheckConstraint(
                "ck_candidate_skills_last_used_year",
                "last_used_year IS NULL OR (last_used_year BETWEEN 1990 AND 2100)");

            t.HasCheckConstraint(
                "ck_candidate_skills_evidence_level",
                "evidence_level IN (0, 1, 2)");
        });

        builder.HasKey(cs => new { cs.CandidateId, cs.SkillId });

        builder.Property(cs => cs.Proficiency)
            .HasConversion<short>()
            .HasColumnType("smallint")
            .IsRequired();

        builder.Property(cs => cs.YearsUsed)
            .HasColumnType("numeric(3,1)");

        builder.Property(cs => cs.LastUsedYear)
            .HasColumnType("smallint");

        builder.Property(cs => cs.EvidenceLevel)
            .HasConversion<short>()
            .HasColumnType("smallint")
            .HasDefaultValueSql("0")
            .IsRequired();

        builder.Property(cs => cs.IsPrimary)
            .HasDefaultValueSql("false")
            .IsRequired();

        builder.HasOne(cs => cs.Candidate)
            .WithMany(c => c.Skills)
            .HasForeignKey(cs => cs.CandidateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cs => cs.Skill)
            .WithMany()
            .HasForeignKey(cs => cs.SkillId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(cs => new { cs.SkillId, cs.Proficiency }, "ix_candskills_skill")
            .HasDatabaseName("ix_candskills_skill");
    }
}
