using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class JobSkillConfiguration : IEntityTypeConfiguration<JobSkill>
{
    public void Configure(EntityTypeBuilder<JobSkill> b)
    {
        b.ToTable("job_skills", t => t.HasCheckConstraint("ck_job_skills_proficiency", "min_proficiency BETWEEN 1 AND 4"));
        b.HasKey(x => new { x.JobId, x.SkillId });
        b.Property(x => x.MinProficiency).HasConversion<short>().HasColumnType("smallint").HasDefaultValue(SkillProficiency.Working);
        b.Property(x => x.IsRequired).HasDefaultValue(true);
        b.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Skill>().WithMany().HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.SkillId, x.JobId }).HasDatabaseName("ix_job_skills_skill_job");
    }
}
