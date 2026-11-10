using HireGameDev.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class StudioSkillConfiguration : IEntityTypeConfiguration<StudioSkill>
{
    public void Configure(EntityTypeBuilder<StudioSkill> builder)
    {
        builder.ToTable("studio_skills");

        builder.HasKey(ss => new { ss.StudioId, ss.SkillId });

        builder.HasOne(ss => ss.Studio)
            .WithMany(s => s.StudioSkills)
            .HasForeignKey(ss => ss.StudioId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ss => ss.Skill)
            .WithMany()
            .HasForeignKey(ss => ss.SkillId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(ss => ss.SkillId)
            .HasDatabaseName("ix_studio_skills_skill_id");
    }
}
