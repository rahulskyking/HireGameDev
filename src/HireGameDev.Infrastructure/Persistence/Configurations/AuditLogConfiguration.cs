using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs", t =>
        {
            t.HasCheckConstraint(
                "ck_audit_logs_actor_type",
                "actor_type IN ('user', 'admin', 'system')");

            t.HasCheckConstraint(
                "ck_audit_logs_admin_reason_required",
                "actor_type <> 'admin' OR (reason IS NOT NULL AND btrim(reason) <> '')");

            t.HasCheckConstraint(
                "ck_audit_logs_action_nonempty",
                "btrim(action) <> ''");
        });

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .UseIdentityAlwaysColumn();

        builder.Property(a => a.ActorType)
            .HasConversion(new SnakeCaseEnumValueConverter<AuditActorType>())
            .HasColumnType("text")
            .HasDefaultValueSql("'user'")
            .IsRequired();

        builder.Property(a => a.Action)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(a => a.EntityType)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(a => a.EntityId)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(a => a.Reason)
            .HasColumnType("text");

        builder.Property(a => a.BeforeData)
            .HasColumnType("jsonb");

        builder.Property(a => a.AfterData)
            .HasColumnType("jsonb");

        builder.Property(a => a.Ip)
            .HasColumnType("inet");

        builder.Property(a => a.CreatedAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(a => new { a.EntityType, a.EntityId }, "ix_audit_entity")
            .HasDatabaseName("ix_audit_entity");

        builder.HasIndex(a => new { a.ActorUserId, a.CreatedAt }, "ix_audit_actor")
            .IsDescending(false, true)
            .HasDatabaseName("ix_audit_actor");

        builder.HasIndex(a => a.CreatedAt, "ix_audit_created_at")
            .IsDescending(true)
            .HasDatabaseName("ix_audit_created_at");
    }
}
