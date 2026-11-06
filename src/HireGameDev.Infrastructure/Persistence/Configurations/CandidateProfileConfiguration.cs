using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class CandidateProfileConfiguration : IEntityTypeConfiguration<CandidateProfile>
{
    public void Configure(EntityTypeBuilder<CandidateProfile> builder)
    {
        builder.ToTable("candidate_profiles", t =>
        {
            t.HasCheckConstraint(
                "ck_candidate_profiles_full_name_len",
                "char_length(btrim(full_name)) BETWEEN 2 AND 80");

            t.HasCheckConstraint(
                "ck_candidate_profiles_headline_len",
                "char_length(btrim(headline)) BETWEEN 3 AND 80");

            t.HasCheckConstraint(
                "ck_candidate_profiles_primary_category",
                "primary_category IN ('programming', 'art', 'design', 'audio', 'qa', 'production')");

            t.HasCheckConstraint(
                "ck_candidate_profiles_phone_e164",
                @"phone IS NULL OR phone ~ '^\+[1-9][0-9]{7,14}$'");

            t.HasCheckConstraint(
                "ck_candidate_profiles_seniority",
                "seniority IN ('fresher', 'junior', 'mid', 'senior', 'lead')");

            t.HasCheckConstraint(
                "ck_candidate_profiles_years_experience",
                "years_experience >= 0 AND years_experience <= 50");

            t.HasCheckConstraint(
                "ck_candidate_profiles_current_ctc_lpa",
                "current_ctc_lpa IS NULL OR current_ctc_lpa >= 0");

            t.HasCheckConstraint(
                "ck_candidate_profiles_expected_ctc_lpa",
                "expected_ctc_lpa IS NULL OR expected_ctc_lpa > 0");

            t.HasCheckConstraint(
                "ck_candidate_profiles_notice_period_days",
                "notice_period_days IS NULL OR notice_period_days IN (0, 15, 30, 45, 60, 90)");

            t.HasCheckConstraint(
                "ck_candidate_profiles_availability",
                "availability IN ('actively_looking', 'open_to_offers', 'not_looking')");

            t.HasCheckConstraint(
                "ck_candidate_profiles_bio_len",
                "bio IS NULL OR char_length(bio) <= 1500");

            t.HasCheckConstraint(
                "ck_candidate_profiles_completeness",
                "completeness >= 0 AND completeness <= 100");

            t.HasCheckConstraint(
                "ck_candidate_profiles_onboarding_step",
                "onboarding_step >= 1 AND onboarding_step <= 5");
        });

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(c => c.UserId)
            .IsRequired();

        builder.Property(c => c.FullName)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(c => c.Headline)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(c => c.PrimaryCategory)
            .HasConversion(new SnakeCaseEnumValueConverter<DisciplineCategory>())
            .HasColumnType("text")
            .IsRequired();

        builder.Property(c => c.PhotoPath)
            .HasColumnType("text");

        builder.Property(c => c.Phone)
            .HasColumnType("text");

        builder.Property(c => c.PhoneVerifiedAt)
            .HasColumnType("timestamptz");

        builder.Property(c => c.CityId)
            .IsRequired();

        builder.Property(c => c.Seniority)
            .HasConversion(new SnakeCaseEnumValueConverter<SeniorityLevel>())
            .HasColumnType("text")
            .HasDefaultValueSql("'fresher'")
            .IsRequired();

        builder.Property(c => c.YearsExperience)
            .HasColumnType("numeric(3,1)")
            .HasDefaultValueSql("0.0")
            .IsRequired();

        builder.Property(c => c.CurrentCtcLpa)
            .HasColumnType("numeric(6,2)");

        builder.Property(c => c.ShowCurrentCtc)
            .HasDefaultValueSql("false")
            .IsRequired();

        builder.Property(c => c.ExpectedCtcLpa)
            .HasColumnType("numeric(6,2)");

        builder.Property(c => c.NoticePeriodDays)
            .HasColumnType("smallint");

        builder.Property(c => c.OpenToRemote)
            .HasDefaultValueSql("false")
            .IsRequired();

        builder.Property(c => c.WillingToRelocate)
            .HasDefaultValueSql("false")
            .IsRequired();

        builder.Property(c => c.Availability)
            .HasConversion(new SnakeCaseEnumValueConverter<CandidateAvailability>())
            .HasColumnType("text")
            .HasDefaultValueSql("'actively_looking'")
            .IsRequired();

        builder.Property(c => c.Bio)
            .HasColumnType("text");

        builder.Property(c => c.Completeness)
            .HasColumnType("smallint")
            .HasDefaultValueSql("0")
            .IsRequired();

        builder.Property(c => c.OnboardingStep)
            .HasColumnType("smallint")
            .HasDefaultValueSql("1")
            .IsRequired();

        builder.Property(c => c.IsDiscoverable)
            .HasDefaultValueSql("true")
            .IsRequired();

        builder.Property(c => c.BoostedUntil)
            .HasColumnType("timestamptz");

        builder.Property(c => c.LastActiveAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(c => c.DeletedAt)
            .HasColumnType("timestamptz");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.City)
            .WithMany()
            .HasForeignKey(c => c.CityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.UserId, "ux_candidate_profiles_user_id")
            .IsUnique()
            .HasDatabaseName("ux_candidate_profiles_user_id");

        builder.HasIndex(c => new { c.PrimaryCategory, c.CityId, c.Seniority }, "ix_cand_discover")
            .HasFilter("is_discoverable AND deleted_at IS NULL")
            .HasDatabaseName("ix_cand_discover");

        builder.HasIndex(c => c.CityId, "ix_candidate_profiles_city_id")
            .HasDatabaseName("ix_candidate_profiles_city_id");
    }
}
