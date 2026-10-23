using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal static class IdentityTablesConfiguration
{
    public static void ConfigureIdentityTables(this ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>(b =>
        {
            b.ToTable("users", t =>
            {
                t.HasCheckConstraint(
                    "ck_users_account_type",
                    "account_type IN ('candidate', 'studio', 'admin')");

                t.HasCheckConstraint(
                    "ck_users_theme_preference",
                    "theme_preference IN ('light', 'dark', 'system')");
            });

            b.Property(u => u.Id)
                .HasDefaultValueSql("gen_random_uuid()");

            b.Property(u => u.AccountType)
                .HasConversion(new SnakeCaseEnumValueConverter<AccountType>())
                .HasColumnType("text")
                .IsRequired();

            b.Property(u => u.DisplayName)
                .HasColumnType("text")
                .IsRequired();

            b.Property(u => u.IsActive)
                .HasDefaultValueSql("true")
                .IsRequired();

            b.Property(u => u.ThemePreference)
                .HasConversion(new SnakeCaseEnumValueConverter<ThemePreference>())
                .HasColumnType("text")
                .HasDefaultValueSql("'system'")
                .IsRequired();

            b.Property(u => u.Timezone)
                .HasColumnType("text")
                .HasDefaultValueSql("'Asia/Kolkata'")
                .IsRequired();

            b.Property(u => u.AgeConfirmedAt)
                .HasColumnType("timestamptz");

            b.Property(u => u.LastLoginAt)
                .HasColumnType("timestamptz");

            b.Property(u => u.CreatedAt)
                .HasColumnType("timestamptz")
                .HasDefaultValueSql("now()")
                .IsRequired();

            b.Property(u => u.UpdatedAt)
                .HasColumnType("timestamptz")
                .HasDefaultValueSql("now()")
                .IsRequired();

            b.Property(u => u.DeletedAt)
                .HasColumnType("timestamptz");
        });

        builder.Entity<ApplicationRole>(b =>
        {
            b.ToTable("roles");

            b.Property(r => r.Id)
                .HasDefaultValueSql("gen_random_uuid()");
        });

        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
    }
}
