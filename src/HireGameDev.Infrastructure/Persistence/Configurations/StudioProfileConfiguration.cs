using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class StudioProfileConfiguration : IEntityTypeConfiguration<StudioProfile>
{
    public void Configure(EntityTypeBuilder<StudioProfile> builder)
    {
        builder.ToTable("studio_profiles", t =>
        {
            t.HasCheckConstraint("ck_studio_profiles_suspension", "(suspended_at IS NULL AND suspended_by_user_id IS NULL AND suspension_reason IS NULL) OR (suspended_at IS NOT NULL AND suspended_by_user_id IS NOT NULL AND suspension_reason IS NOT NULL AND char_length(btrim(suspension_reason)) BETWEEN 1 AND 1000)");
            t.HasCheckConstraint(
                "ck_studio_profiles_name_len",
                "char_length(btrim(name)) BETWEEN 2 AND 100");

            t.HasCheckConstraint(
                "ck_studio_profiles_slug",
                @"char_length(slug) BETWEEN 2 AND 100 AND slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");

            t.HasCheckConstraint(
                "ck_studio_profiles_website",
                @"website IS NULL OR (char_length(website) <= 500 AND website ~ '^https://[^/\s:@]+\.[^/\s]+')");

            t.HasCheckConstraint(
                "ck_studio_profiles_description_len",
                "description IS NULL OR char_length(description) <= 3000");

            t.HasCheckConstraint(
                "ck_studio_profiles_size_band",
                "size_band IN ('1-10', '11-50', '51-200', '200+')");

            t.HasCheckConstraint(
                "ck_studio_profiles_studio_type",
                "studio_type IN ('indie', 'game_studio', 'outsourcing', 'publisher', 'other')");

            t.HasCheckConstraint(
                "ck_studio_profiles_founded_year",
                "founded_year IS NULL OR (founded_year BETWEEN 1950 AND 2100)");

            t.HasCheckConstraint(
                "ck_studio_profiles_culture_tags",
                "cardinality(culture_tags) <= 6 AND culture_tags <@ ARRAY['remote_friendly', 'hybrid_work', 'flexible_hours', 'no_crunch', 'indie_spirit', 'original_ip', 'co_dev_partner', 'learning_budget', 'health_insurance', 'relocation_support', 'revenue_share', 'diverse_inclusive']::text[]");

            t.HasCheckConstraint(
                "ck_studio_profiles_gstin",
                @"gstin IS NULL OR gstin ~ '^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$'");

            t.HasCheckConstraint(
                "ck_studio_profiles_verification_status",
                "verification_status IN ('unverified', 'pending', 'verified', 'rejected')");

            t.HasCheckConstraint(
                "ck_studio_profiles_verification_proof_urls",
                "cardinality(verification_proof_urls) <= 5");

            t.HasCheckConstraint(
                "ck_studio_profiles_verification_applicant_notes_len",
                "verification_applicant_notes IS NULL OR char_length(verification_applicant_notes) <= 1000");

            t.HasCheckConstraint(
                "ck_studio_profiles_pending_identity_consistency",
                "(pending_identity_requested_at IS NULL AND pending_identity_name IS NULL AND pending_identity_slug IS NULL AND pending_identity_gstin IS NULL) OR (verification_status = 'verified' AND pending_identity_requested_at IS NOT NULL AND pending_identity_name IS NOT NULL AND char_length(btrim(pending_identity_name)) BETWEEN 2 AND 100 AND pending_identity_slug IS NOT NULL)");

            t.HasCheckConstraint(
                "ck_studio_profiles_pending_identity_slug",
                @"pending_identity_slug IS NULL OR (char_length(pending_identity_slug) BETWEEN 2 AND 100 AND pending_identity_slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$')");

            t.HasCheckConstraint(
                "ck_studio_profiles_pending_identity_gstin",
                @"pending_identity_gstin IS NULL OR pending_identity_gstin ~ '^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$'");

            t.HasCheckConstraint(
                "ck_studio_profiles_verification_notes_len",
                "verification_notes IS NULL OR char_length(verification_notes) <= 2000");

            t.HasCheckConstraint(
                "ck_studio_profiles_apps_90d",
                "apps_90d >= 0");

            t.HasCheckConstraint(
                "ck_studio_profiles_response_rate_90d",
                "response_rate_90d IS NULL OR (response_rate_90d >= 0 AND response_rate_90d <= 100)");

            t.HasCheckConstraint(
                "ck_studio_profiles_median_response_hours",
                "median_first_response_hours IS NULL OR median_first_response_hours >= 0");
        });

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(s => s.OwnerUserId)
            .IsRequired();

        builder.Property(s => s.Name)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(s => s.Slug)
            .HasColumnType("citext")
            .IsRequired();

        builder.Property(s => s.LogoPath)
            .HasColumnType("text");

        builder.Property(s => s.Website)
            .HasColumnType("text");

        builder.Property(s => s.Description)
            .HasColumnType("text");

        builder.Property(s => s.CityId)
            .HasColumnType("integer");

        builder.Property(s => s.SizeBand)
            .HasConversion(new StudioSizeBandValueConverter())
            .HasColumnType("text")
            .IsRequired();

        builder.Property(s => s.StudioType)
            .HasConversion(new SnakeCaseEnumValueConverter<StudioType>())
            .HasColumnType("text")
            .IsRequired();

        builder.Property(s => s.FoundedYear)
            .HasColumnType("smallint");

        builder.Property(s => s.CultureTags)
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]")
            .IsRequired();

        builder.Property(s => s.Gstin)
            .HasColumnType("text");

        builder.Property(s => s.CompanyEmailDomain)
            .HasColumnType("text");

        builder.Property(s => s.VerificationStatus)
            .HasConversion(new SnakeCaseEnumValueConverter<StudioVerificationStatus>())
            .HasColumnType("text")
            .HasDefaultValueSql("'unverified'")
            .IsRequired();

        builder.Property(s => s.VerificationSubmittedAt)
            .HasColumnName("verification_submitted_at")
            .HasColumnType("timestamptz");

        builder.Property(s => s.VerificationProofUrls)
            .HasColumnName("verification_proof_urls")
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]")
            .IsRequired();

        builder.Property(s => s.VerificationApplicantNotes)
            .HasColumnName("verification_applicant_notes")
            .HasColumnType("text");

        builder.Property(s => s.PendingIdentityName)
            .HasColumnName("pending_identity_name")
            .HasColumnType("text");

        builder.Property(s => s.PendingIdentitySlug)
            .HasColumnName("pending_identity_slug")
            .HasColumnType("citext");

        builder.Property(s => s.PendingIdentityGstin)
            .HasColumnName("pending_identity_gstin")
            .HasColumnType("text");

        builder.Property(s => s.PendingIdentityRequestedAt)
            .HasColumnName("pending_identity_requested_at")
            .HasColumnType("timestamptz");

        builder.Property(s => s.VerifiedAt)
            .HasColumnType("timestamptz");

        builder.Property(s => s.VerifiedBy)
            .HasColumnType("uuid");

        builder.Property(s => s.VerificationNotes)
            .HasColumnType("text");

        builder.Property(s => s.Apps90d)
            .HasColumnName("apps_90d")
            .HasColumnType("integer")
            .HasDefaultValueSql("0")
            .IsRequired();

        builder.Property(s => s.ResponseRate90d)
            .HasColumnName("response_rate_90d")
            .HasColumnType("numeric(5,2)");

        builder.Property(s => s.MedianFirstResponseHours)
            .HasColumnType("numeric(6,1)");

        builder.Property(s => s.MetricsUpdatedAt)
            .HasColumnType("timestamptz");

        builder.Property(s => s.CreatedAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(s => s.DeletedAt)
            .HasColumnType("timestamptz");

        builder.Property(x => x.SuspendedAt).HasColumnType("timestamptz");
        builder.Property(x => x.SuspensionReason).HasMaxLength(1000);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SuspendedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.City)
            .WithMany()
            .HasForeignKey(s => s.CityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.VerifiedBy)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(s => s.Slug, "ux_studio_profiles_slug")
            .IsUnique()
            .HasDatabaseName("ux_studio_profiles_slug");

        builder.HasIndex(s => s.OwnerUserId)
            .HasDatabaseName("ix_studio_profiles_owner_user_id");

        builder.HasIndex(s => s.OwnerUserId, "ux_studio_profiles_owner_active")
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ux_studio_profiles_owner_active");

        builder.HasIndex(s => s.CityId)
            .HasDatabaseName("ix_studio_profiles_city_id");

        builder.HasIndex(s => s.VerifiedBy)
            .HasDatabaseName("ix_studio_profiles_verified_by");

        builder.HasIndex(s => s.VerificationStatus, "ix_studio_profiles_verification_status")
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_studio_profiles_verification_status");

        builder.HasIndex(
                s => new { s.VerificationStatus, s.VerificationSubmittedAt },
                "ix_studio_profiles_verification_queue")
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_studio_profiles_verification_queue");

        builder.HasIndex(s => s.PendingIdentitySlug, "ux_studio_profiles_pending_identity_slug")
            .IsUnique()
            .HasFilter("deleted_at IS NULL AND pending_identity_slug IS NOT NULL")
            .HasDatabaseName("ux_studio_profiles_pending_identity_slug");

        builder.HasIndex(s => s.PendingIdentityRequestedAt, "ix_studio_profiles_pending_identity_queue")
            .HasFilter("deleted_at IS NULL AND pending_identity_requested_at IS NOT NULL")
            .HasDatabaseName("ix_studio_profiles_pending_identity_queue");
    }
}
