using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class SkillConfiguration : IEntityTypeConfiguration<Skill>
{
    public void Configure(EntityTypeBuilder<Skill> builder)
    {
        builder.ToTable("skills", t =>
        {
            t.HasCheckConstraint(
                "ck_skills_category",
                "category IN ('engine', 'language', 'art_tool', 'design_audio_tool', 'devops', 'genre', 'platform', 'role')");

            t.HasCheckConstraint(
                "ck_skills_idf_weight",
                "idf_weight >= 0.50 AND idf_weight <= 2.00");

            t.HasCheckConstraint(
                "ck_skills_no_self_parent",
                "parent_skill_id IS NULL OR parent_skill_id <> id");
        });

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .UseIdentityAlwaysColumn();

        builder.Property(s => s.Name)
            .HasColumnType("citext")
            .IsRequired();

        builder.Property(s => s.Slug)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(s => s.Category)
            .HasConversion(new SnakeCaseEnumValueConverter<SkillCategory>())
            .HasColumnType("text")
            .IsRequired();

        builder.Property(s => s.Subcategory)
            .HasColumnType("text");

        builder.Property(s => s.Aliases)
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]")
            .IsRequired();

        builder.Property(s => s.IdfWeight)
            .HasColumnType("numeric(4,2)")
            .HasDefaultValueSql("1.00")
            .IsRequired();

        builder.Property(s => s.IsActive)
            .HasDefaultValueSql("true")
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne(s => s.ParentSkill)
            .WithMany(s => s.ChildSkills)
            .HasForeignKey(s => s.ParentSkillId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => s.Name, "ux_skills_name")
            .IsUnique()
            .HasDatabaseName("ux_skills_name");

        builder.HasIndex(s => s.Slug, "ux_skills_slug")
            .IsUnique()
            .HasDatabaseName("ux_skills_slug");

        builder.HasIndex(s => s.Category, "ix_skills_category")
            .HasFilter("is_active")
            .HasDatabaseName("ix_skills_category");

        builder.HasIndex(s => s.Name, "ix_skills_name_trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasDatabaseName("ix_skills_name_trgm");

        builder.HasIndex(s => s.Aliases, "ix_skills_aliases_gin")
            .HasMethod("gin")
            .HasDatabaseName("ix_skills_aliases_gin");

        builder.HasIndex(s => s.ParentSkillId, "ix_skills_parent_skill_id")
            .HasDatabaseName("ix_skills_parent_skill_id");
    }
}
