using HireGameDev.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class PortfolioLinkSkillConfiguration : IEntityTypeConfiguration<PortfolioLinkSkill>
{
    public void Configure(EntityTypeBuilder<PortfolioLinkSkill> builder)
    {
        builder.ToTable("portfolio_link_skills");

        builder.HasKey(pls => new { pls.PortfolioLinkId, pls.SkillId });

        builder.HasOne(pls => pls.PortfolioLink)
            .WithMany(pl => pl.EvidencedSkills)
            .HasForeignKey(pls => pls.PortfolioLinkId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pls => pls.Skill)
            .WithMany()
            .HasForeignKey(pls => pls.SkillId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(pls => pls.SkillId)
            .HasDatabaseName("ix_portfoliolinkskills_skill");
    }
}
