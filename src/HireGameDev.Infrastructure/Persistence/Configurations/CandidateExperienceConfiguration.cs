using HireGameDev.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class CandidateExperienceConfiguration : IEntityTypeConfiguration<CandidateExperience>
{
    public void Configure(EntityTypeBuilder<CandidateExperience> builder)
    {
        builder.ToTable("candidate_experiences", t =>
        {
            t.HasCheckConstraint(
                "ck_candidate_experiences_company_name_len",
                "char_length(btrim(company_name)) BETWEEN 1 AND 160");

            t.HasCheckConstraint(
                "ck_candidate_experiences_title_len",
                "char_length(btrim(title)) BETWEEN 1 AND 160");

            t.HasCheckConstraint(
                "ck_candidate_experiences_description_len",
                "description IS NULL OR char_length(description) <= 1500");

            t.HasCheckConstraint(
                "ck_candidate_experiences_date_order",
                "end_date IS NULL OR end_date >= start_date");

            t.HasCheckConstraint(
                "ck_candidate_experiences_current_end_date",
                "NOT (is_current AND end_date IS NOT NULL)");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.CandidateId)
            .IsRequired();

        builder.Property(e => e.CompanyName)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Title)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.StartDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(e => e.EndDate)
            .HasColumnType("date");

        builder.Property(e => e.IsCurrent)
            .HasDefaultValueSql("false")
            .IsRequired();

        builder.Property(e => e.Description)
            .HasColumnType("text");

        builder.HasOne(e => e.Candidate)
            .WithMany(c => c.Experiences)
            .HasForeignKey(e => e.CandidateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(
                e => new { e.CandidateId, e.IsCurrent, e.StartDate },
                "ix_candexp_candidate_timeline")
            .HasDatabaseName("ix_candexp_candidate_timeline")
            .IsDescending(false, true, true);
    }
}
