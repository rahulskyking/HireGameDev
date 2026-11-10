using HireGameDev.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class StudioTitleConfiguration : IEntityTypeConfiguration<StudioTitle>
{
    public void Configure(EntityTypeBuilder<StudioTitle> builder)
    {
        builder.ToTable("studio_titles", t =>
        {
            t.HasCheckConstraint(
                "ck_studio_titles_game_name_len",
                "char_length(btrim(game_name)) BETWEEN 1 AND 160");

            t.HasCheckConstraint(
                "ck_studio_titles_release_year",
                "release_year IS NULL OR (release_year BETWEEN 1980 AND 2100)");

            t.HasCheckConstraint(
                "ck_studio_titles_platforms",
                "cardinality(platforms) <= 5 AND platforms <@ ARRAY['pc', 'android', 'ios', 'console', 'web']::text[]");

            t.HasCheckConstraint(
                "ck_studio_titles_store_url",
                @"store_url IS NULL OR (char_length(store_url) <= 500 AND store_url ~ '^https://[^/\s:@]+\.[^/\s]+')");

            t.HasCheckConstraint(
                "ck_studio_titles_sort_order",
                "sort_order >= 0");
        });

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(t => t.StudioId)
            .IsRequired();

        builder.Property(t => t.GameName)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(t => t.ReleaseYear)
            .HasColumnType("smallint");

        builder.Property(t => t.Platforms)
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]")
            .IsRequired();

        builder.Property(t => t.StoreUrl)
            .HasColumnType("text");

        builder.Property(t => t.IgdbId)
            .HasColumnType("bigint");

        builder.Property(t => t.SortOrder)
            .HasColumnType("smallint")
            .HasDefaultValueSql("0")
            .IsRequired();

        builder.HasOne(t => t.Studio)
            .WithMany(s => s.Titles)
            .HasForeignKey(t => t.StudioId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(
                t => new { t.StudioId, t.SortOrder, t.ReleaseYear },
                "ix_studio_titles_studio_sort")
            .HasDatabaseName("ix_studio_titles_studio_sort")
            .IsDescending(false, false, true);
    }
}
