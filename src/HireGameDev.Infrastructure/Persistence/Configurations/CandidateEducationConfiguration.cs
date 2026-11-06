using HireGameDev.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class CandidateEducationConfiguration : IEntityTypeConfiguration<CandidateEducation>
{
    public void Configure(EntityTypeBuilder<CandidateEducation> builder)
    {
        builder.ToTable("candidate_education", t =>
        {
            t.HasCheckConstraint(
                "ck_candidate_education_institution_len",
                "char_length(btrim(institution)) BETWEEN 1 AND 200");

            t.HasCheckConstraint(
                "ck_candidate_education_degree_len",
                "char_length(btrim(degree)) BETWEEN 1 AND 160");

            t.HasCheckConstraint(
                "ck_candidate_education_field_len",
                "field IS NULL OR char_length(field) <= 160");

            t.HasCheckConstraint(
                "ck_candidate_education_start_year",
                "start_year IS NULL OR (start_year BETWEEN 1950 AND 2100)");

            t.HasCheckConstraint(
                "ck_candidate_education_end_year",
                "end_year IS NULL OR (end_year BETWEEN 1950 AND 2100)");

            t.HasCheckConstraint(
                "ck_candidate_education_year_order",
                "start_year IS NULL OR end_year IS NULL OR end_year >= start_year");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.CandidateId)
            .IsRequired();

        builder.Property(e => e.Institution)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Degree)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Field)
            .HasColumnType("text");

        builder.Property(e => e.StartYear)
            .HasColumnType("smallint");

        builder.Property(e => e.EndYear)
            .HasColumnType("smallint");

        builder.HasOne(e => e.Candidate)
            .WithMany(c => c.Education)
            .HasForeignKey(e => e.CandidateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(
                e => new { e.CandidateId, e.EndYear },
                "ix_candedu_candidate_timeline")
            .HasDatabaseName("ix_candedu_candidate_timeline")
            .IsDescending(false, true);
    }
}
