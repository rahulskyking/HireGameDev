using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class ShippedTitleConfiguration : IEntityTypeConfiguration<ShippedTitle>
{
    public void Configure(EntityTypeBuilder<ShippedTitle> builder)
    {
        builder.ToTable("shipped_titles", t =>
        {
            t.HasCheckConstraint(
                "ck_shipped_titles_game_name_len",
                "char_length(btrim(game_name)) BETWEEN 1 AND 160");

            t.HasCheckConstraint(
                "ck_shipped_titles_studio_name_len",
                "char_length(btrim(studio_name)) BETWEEN 1 AND 160");

            t.HasCheckConstraint(
                "ck_shipped_titles_role_len",
                "char_length(btrim(role)) BETWEEN 1 AND 120");

            t.HasCheckConstraint(
                "ck_shipped_titles_platforms",
                "cardinality(platforms) <= 5 AND platforms <@ ARRAY['pc', 'android', 'ios', 'console', 'web']::text[]");

            t.HasCheckConstraint(
                "ck_shipped_titles_release_year",
                "release_year IS NULL OR (release_year BETWEEN 1990 AND 2100)");

            t.HasCheckConstraint(
                "ck_shipped_titles_store_url",
                @"store_url IS NULL OR (char_length(store_url) <= 500 AND store_url ~ '^https://[^/\s:@]+\.[^/\s]+')");

            t.HasCheckConstraint(
                "ck_shipped_titles_verification",
                "verification IN ('none', 'title_verified', 'credit_verified')");

            t.HasCheckConstraint(
                "ck_shipped_titles_nda_studio_hide",
                "NOT hide_studio_in_public OR is_nda");

            t.HasCheckConstraint(
                "ck_shipped_titles_sort_order",
                "sort_order >= 0");
        });

        builder.HasKey(st => st.Id);

        builder.Property(st => st.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(st => st.CandidateId)
            .IsRequired();

        builder.Property(st => st.GameName)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(st => st.StudioName)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(st => st.Role)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(st => st.Platforms)
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]")
            .IsRequired();

        builder.Property(st => st.EngineSkillId)
            .HasColumnType("integer");

        builder.Property(st => st.ReleaseYear)
            .HasColumnType("smallint");

        builder.Property(st => st.StoreUrl)
            .HasColumnType("text");

        builder.Property(st => st.IgdbId)
            .HasColumnType("bigint");

        builder.Property(st => st.Verification)
            .HasConversion(new SnakeCaseEnumValueConverter<ShippedTitleVerification>())
            .HasColumnType("text")
            .HasDefaultValueSql("'none'")
            .IsRequired();

        builder.Property(st => st.VerifiedAt)
            .HasColumnType("timestamptz");

        builder.Property(st => st.IsNda)
            .HasDefaultValueSql("false")
            .IsRequired();

        builder.Property(st => st.HideStudioInPublic)
            .HasDefaultValueSql("false")
            .IsRequired();

        builder.Property(st => st.SortOrder)
            .HasColumnType("smallint")
            .HasDefaultValueSql("0")
            .IsRequired();

        builder.HasOne(st => st.Candidate)
            .WithMany(c => c.ShippedTitles)
            .HasForeignKey(st => st.CandidateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(st => st.EngineSkill)
            .WithMany()
            .HasForeignKey(st => st.EngineSkillId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(
                st => new { st.CandidateId, st.SortOrder, st.ReleaseYear },
                "ix_shippedtitles_candidate_sort")
            .HasDatabaseName("ix_shippedtitles_candidate_sort")
            .IsDescending(false, false, true);

        builder.HasIndex(st => st.EngineSkillId)
            .HasDatabaseName("ix_shippedtitles_engine_skill");
    }
}
