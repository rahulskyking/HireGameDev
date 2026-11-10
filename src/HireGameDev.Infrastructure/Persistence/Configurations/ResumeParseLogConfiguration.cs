using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class ResumeParseLogConfiguration : IEntityTypeConfiguration<ResumeParseLog>
{
    public void Configure(EntityTypeBuilder<ResumeParseLog> builder)
    {
        builder.ToTable("resume_parse_logs", t =>
        {
            t.HasCheckConstraint(
                "ck_resume_parse_logs_source",
                "source IN ('pdf', 'linkedin_pdf')");

            t.HasCheckConstraint(
                "ck_resume_parse_logs_status",
                "status IN ('ok', 'failed', 'rejected')");

            t.HasCheckConstraint(
                "ck_resume_parse_logs_input_chars",
                "input_chars IS NULL OR input_chars >= 0");

            t.HasCheckConstraint(
                "ck_resume_parse_logs_duration_ms",
                "duration_ms IS NULL OR duration_ms >= 0");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .UseIdentityAlwaysColumn();

        builder.Property(r => r.UserId)
            .IsRequired();

        builder.Property(r => r.Source)
            .HasConversion(new SnakeCaseEnumValueConverter<ResumeParseSource>())
            .HasColumnType("text")
            .IsRequired();

        builder.Property(r => r.Status)
            .HasConversion(new SnakeCaseEnumValueConverter<ResumeParseStatus>())
            .HasColumnType("text")
            .IsRequired();

        builder.Property(r => r.InputChars)
            .HasColumnType("integer");

        builder.Property(r => r.DurationMs)
            .HasColumnType("integer");

        builder.Property(r => r.CreatedAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.UserId, r.CreatedAt }, "ix_parse_user_day")
            .IsDescending(false, true)
            .HasDatabaseName("ix_parse_user_day");
    }
}
