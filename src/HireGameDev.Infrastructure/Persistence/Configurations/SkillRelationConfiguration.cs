using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class SkillRelationConfiguration : IEntityTypeConfiguration<SkillRelation>
{
    public void Configure(EntityTypeBuilder<SkillRelation> builder)
    {
        builder.ToTable("skill_relations", t =>
        {
            t.HasCheckConstraint(
                "ck_skill_relations_weight",
                "weight > 0 AND weight <= 1");

            t.HasCheckConstraint(
                "ck_skill_relations_not_self",
                "skill_id <> related_skill_id");

            t.HasCheckConstraint(
                "ck_skill_relations_relation_type",
                "relation_type IN ('implies', 'superset', 'peer_tool', 'transferable')");
        });

        builder.HasKey(r => new { r.SkillId, r.RelatedSkillId });

        builder.Property(r => r.RelationType)
            .HasConversion(new SnakeCaseEnumValueConverter<SkillRelationType>())
            .HasColumnType("text")
            .IsRequired();

        builder.Property(r => r.Weight)
            .HasColumnType("numeric(3,2)")
            .IsRequired();

        builder.HasOne(r => r.Skill)
            .WithMany(s => s.OutgoingRelations)
            .HasForeignKey(r => r.SkillId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.RelatedSkill)
            .WithMany(s => s.IncomingRelations)
            .HasForeignKey(r => r.RelatedSkillId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.RelatedSkillId, "ix_skill_relations_related_skill_id")
            .HasDatabaseName("ix_skill_relations_related_skill_id");
    }
}
