using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class PortfolioLinkConfiguration : IEntityTypeConfiguration<PortfolioLink>
{
    public void Configure(EntityTypeBuilder<PortfolioLink> builder)
    {
        builder.ToTable("portfolio_links", t =>
        {
            t.HasCheckConstraint(
                "ck_portfolio_links_link_type",
                "link_type IN ('github', 'itchio', 'artstation', 'behance', 'youtube', 'website', 'linkedin', 'other')");

            t.HasCheckConstraint(
                "ck_portfolio_links_url",
                @"char_length(url) <= 500 AND url ~ '^https://[^/\s:@]+\.[^/\s]+'");

            t.HasCheckConstraint(
                "ck_portfolio_links_title_len",
                "title IS NULL OR char_length(title) <= 160");

            t.HasCheckConstraint(
                "ck_portfolio_links_sort_order",
                "sort_order >= 0");
        });

        builder.HasKey(pl => pl.Id);

        builder.Property(pl => pl.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(pl => pl.CandidateId)
            .IsRequired();

        builder.Property(pl => pl.LinkType)
            .HasConversion(new SnakeCaseEnumValueConverter<PortfolioLinkType>())
            .HasColumnType("text")
            .IsRequired();

        builder.Property(pl => pl.Url)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(pl => pl.Title)
            .HasColumnType("text");

        builder.Property(pl => pl.SortOrder)
            .HasColumnType("smallint")
            .HasDefaultValueSql("0")
            .IsRequired();

        builder.HasOne(pl => pl.Candidate)
            .WithMany(c => c.PortfolioLinks)
            .HasForeignKey(pl => pl.CandidateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(
                pl => new { pl.CandidateId, pl.SortOrder },
                "ix_portfoliolinks_candidate_sort")
            .HasDatabaseName("ix_portfoliolinks_candidate_sort");
    }
}
