using HireGameDev.Domain.Entities;
using HireGameDev.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("app_settings", t =>
        {
            t.HasCheckConstraint(
                "ck_app_settings_key_nonempty",
                "btrim(key) <> ''");
        });

        builder.HasKey(s => s.Key);

        builder.Property(s => s.Key)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(s => s.Value)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(s => s.Description)
            .HasColumnType("text");

        builder.Property(s => s.UpdatedAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.UpdatedBy)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(s => s.UpdatedBy, "ix_app_settings_updated_by")
            .HasDatabaseName("ix_app_settings_updated_by");
    }
}
