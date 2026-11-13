using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> b)
    {
        b.ToTable("jobs", t =>
        {
            t.HasCheckConstraint("ck_jobs_slug", "char_length(slug) BETWEEN 2 AND 160 AND slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
            t.HasCheckConstraint("ck_jobs_lengths", "(title IS NULL OR char_length(title) <= 100) AND (description IS NULL OR char_length(description) <= 8000) AND (screening_question IS NULL OR char_length(screening_question) <= 300)");
            t.HasCheckConstraint("ck_jobs_ranges", "(min_years IS NULL OR min_years >= 0) AND (max_years IS NULL OR max_years >= 0) AND (min_years IS NULL OR max_years IS NULL OR max_years >= min_years) AND (salary_min IS NULL OR salary_min > 0) AND (salary_max IS NULL OR salary_max > 0) AND (salary_min IS NULL OR salary_max IS NULL OR salary_max >= salary_min)");
            t.HasCheckConstraint("ck_jobs_capacity", "openings BETWEEN 1 AND 50 AND application_cap > 0 AND applications_count >= 0");
            t.HasCheckConstraint("ck_jobs_notice", "max_notice_days IS NULL OR max_notice_days IN (0,15,30,45,60,90)");
            t.HasCheckConstraint("ck_jobs_dates", "(expires_at IS NULL OR (published_at IS NOT NULL AND expires_at > published_at)) AND (published_at IS NULL OR (first_published_at IS NOT NULL AND published_at >= first_published_at))");
            t.HasCheckConstraint("ck_jobs_published", "status NOT IN ('active','paused','full','expired','removed') OR (first_published_at IS NOT NULL AND published_at IS NOT NULL AND expires_at IS NOT NULL)");
            t.HasCheckConstraint("ck_jobs_closed", "(status <> 'closed' OR closed_at IS NOT NULL) AND (closed_at IS NULL OR status IN ('closed','removed'))");
            t.HasCheckConstraint("ck_jobs_removed", "(status = 'removed' AND removed_at IS NOT NULL AND removed_by_user_id IS NOT NULL AND removal_reason IS NOT NULL AND char_length(btrim(removal_reason)) BETWEEN 1 AND 1000) OR (status <> 'removed' AND removed_at IS NULL AND removed_by_user_id IS NULL AND removal_reason IS NULL)");
            t.HasCheckConstraint("ck_jobs_ready", "status NOT IN ('active','paused','full','expired') OR (title IS NOT NULL AND char_length(btrim(title)) BETWEEN 5 AND 100 AND description IS NOT NULL AND char_length(btrim(description)) BETWEEN 100 AND 8000 AND category IS NOT NULL AND job_type IS NOT NULL AND seniority_min IS NOT NULL AND seniority_max IS NOT NULL AND min_years IS NOT NULL AND work_mode IS NOT NULL AND pay_unit IS NOT NULL AND salary_min IS NOT NULL AND salary_max IS NOT NULL AND normalized_title IS NOT NULL AND content_fingerprint IS NOT NULL AND ((work_mode = 'remote' AND city_id IS NULL) OR (work_mode IN ('onsite','hybrid') AND city_id IS NOT NULL)))");
            t.HasCheckConstraint("ck_jobs_fingerprint", "content_fingerprint IS NULL OR content_fingerprint ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("ck_jobs_seniority_order", "seniority_min IS NULL OR seniority_max IS NULL OR array_position(ARRAY['fresher','junior','mid','senior','lead']::text[], seniority_min) <= array_position(ARRAY['fresher','junior','mid','senior','lead']::text[], seniority_max)");
            t.HasCheckConstraint("ck_jobs_status", "status IN ('draft','active','paused','full','expired','closed','removed')");
            t.HasCheckConstraint("ck_jobs_category", "category IN ('programming','art','design','audio','qa','production')");
            t.HasCheckConstraint("ck_jobs_job_type", "job_type IN ('full_time','contract','internship','part_time')");
            t.HasCheckConstraint("ck_jobs_seniority_min", "seniority_min IN ('fresher','junior','mid','senior','lead')");
            t.HasCheckConstraint("ck_jobs_seniority_max", "seniority_max IN ('fresher','junior','mid','senior','lead')");
            t.HasCheckConstraint("ck_jobs_work_mode", "work_mode IN ('onsite','hybrid','remote')");
            t.HasCheckConstraint("ck_jobs_pay_unit", "pay_unit IN ('lpa','monthly_inr')");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion(new SnakeCaseEnumValueConverter<JobStatus>()).HasColumnType("text");
        b.Property(x => x.Category).HasConversion(new SnakeCaseEnumValueConverter<DisciplineCategory>()).HasColumnType("text");
        b.Property(x => x.JobType).HasConversion(new SnakeCaseEnumValueConverter<JobType>()).HasColumnType("text");
        b.Property(x => x.SeniorityMin).HasConversion(new SnakeCaseEnumValueConverter<SeniorityLevel>()).HasColumnType("text");
        b.Property(x => x.SeniorityMax).HasConversion(new SnakeCaseEnumValueConverter<SeniorityLevel>()).HasColumnType("text");
        b.Property(x => x.WorkMode).HasConversion(new SnakeCaseEnumValueConverter<JobWorkMode>()).HasColumnType("text");
        b.Property(x => x.PayUnit).HasConversion(new SnakeCaseEnumValueConverter<JobPayUnit>()).HasColumnType("text");
        b.Property(x => x.Status).HasDefaultValue(JobStatus.Draft);
        b.Property(x => x.Title).HasMaxLength(100);
        b.Property(x => x.Slug).HasMaxLength(160);
        b.Property(x => x.Description).HasMaxLength(8000);
        b.Property(x => x.ScreeningQuestion).HasMaxLength(300);
        b.Property(x => x.NormalizedTitle).HasMaxLength(100);
        b.Property(x => x.ContentFingerprint).HasMaxLength(64);
        b.Property(x => x.RemovalReason).HasMaxLength(1000);
        b.Property(x => x.Slug).IsRequired();
        b.Property(x => x.MinYears).HasPrecision(3, 1);
        b.Property(x => x.MaxYears).HasPrecision(3, 1);
        b.Property(x => x.SalaryMin).HasPrecision(10, 2);
        b.Property(x => x.SalaryMax).HasPrecision(10, 2);
        b.Property(x => x.Openings).HasDefaultValue((short)1);
        b.Property(x => x.ApplicationCap).HasDefaultValue((short)50);
        b.Property(x => x.ApplicationsCount).HasDefaultValue(0);
        b.Property(x => x.RelocationAssistance).HasDefaultValue(false);
        b.Property(x => x.FresherFriendly).HasDefaultValue(false);
        b.Property(x => x.FirstPublishedAt).HasColumnType("timestamptz");
        b.Property(x => x.PublishedAt).HasColumnType("timestamptz");
        b.Property(x => x.ExpiresAt).HasColumnType("timestamptz");
        b.Property(x => x.ClosedAt).HasColumnType("timestamptz");
        b.Property(x => x.RemovedAt).HasColumnType("timestamptz");
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz").HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz").HasDefaultValueSql("now()");
        b.Property(x => x.Version).IsRowVersion(); // Npgsql maps uint rowversion to PostgreSQL xmin.
        b.HasOne<StudioProfile>().WithMany().HasForeignKey(x => x.StudioId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.PostedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RemovedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<City>().WithMany().HasForeignKey(x => x.CityId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("ux_jobs_slug");
        b.HasIndex(x => new { x.StudioId, x.Status, x.UpdatedAt, x.Id }).IsDescending(false, false, true, false).HasDatabaseName("ix_jobs_studio_status_updated");
        b.HasIndex(x => x.ExpiresAt).HasFilter("status IN ('active','paused','full')").HasDatabaseName("ix_jobs_expiry");
        b.HasIndex(x => new { x.StudioId, x.NormalizedTitle }).HasDatabaseName("ix_jobs_studio_title");
        b.HasIndex(x => new { x.StudioId, x.ContentFingerprint }).HasDatabaseName("ix_jobs_studio_fingerprint");
    }
}
