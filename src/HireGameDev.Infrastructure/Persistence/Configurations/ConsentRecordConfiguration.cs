using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class ConsentRecordConfiguration : IEntityTypeConfiguration<ConsentRecord>
{
    public void Configure(EntityTypeBuilder<ConsentRecord> builder)
    {
        builder.ToTable("consent_records", t =>
        {
            t.HasCheckConstraint(
                "ck_consent_records_consent_type",
                "consent_type IN ('terms', 'privacy', 'cookies', 'ai_processing', 'marketing')");

            t.HasCheckConstraint(
                "ck_consent_records_version_nonempty",
                "btrim(version) <> ''");
        });

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .UseIdentityAlwaysColumn();

        builder.Property(c => c.ConsentType)
            .HasConversion(new SnakeCaseEnumValueConverter<ConsentType>())
            .HasColumnType("text")
            .IsRequired();

        builder.Property(c => c.Version)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(c => c.AcceptedAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(c => c.Ip)
            .HasColumnType("inet");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.UserId, c.ConsentType }, "ix_consent_user")
            .HasDatabaseName("ix_consent_user");
    }
}
