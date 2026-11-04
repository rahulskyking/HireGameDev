using HireGameDev.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class CityConfiguration : IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> builder)
    {
        builder.ToTable("cities", t =>
        {
            t.HasCheckConstraint(
                "ck_cities_country_code_len",
                "char_length(country_code) = 2");
        });

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .UseIdentityAlwaysColumn();

        builder.Property(c => c.Name)
            .HasColumnType("citext")
            .IsRequired();

        builder.Property(c => c.Slug)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(c => c.State)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(c => c.StateCode)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(c => c.CountryCode)
            .HasColumnType("char(2)")
            .HasDefaultValueSql("'IN'")
            .IsRequired();

        builder.Property(c => c.MetroGroup)
            .HasColumnType("text");

        builder.Property(c => c.Aliases)
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]")
            .IsRequired();

        builder.Property(c => c.IsHub)
            .HasDefaultValueSql("false")
            .IsRequired();

        builder.Property(c => c.SortOrder)
            .HasColumnType("smallint")
            .HasDefaultValueSql("100")
            .IsRequired();

        builder.Property(c => c.IsActive)
            .HasDefaultValueSql("true")
            .IsRequired();

        builder.HasIndex(c => c.Slug, "ux_cities_slug")
            .IsUnique()
            .HasDatabaseName("ux_cities_slug");

        builder.HasIndex(c => new { c.Name, c.State }, "ux_cities_name_state")
            .IsUnique()
            .HasDatabaseName("ux_cities_name_state");

        builder.HasIndex(c => c.Aliases, "ix_cities_aliases_gin")
            .HasMethod("gin")
            .HasDatabaseName("ix_cities_aliases_gin");

        builder.HasIndex(c => new { c.IsHub, c.SortOrder }, "ix_cities_hub_sort")
            .HasFilter("is_active")
            .HasDatabaseName("ix_cities_hub_sort");
    }
}
