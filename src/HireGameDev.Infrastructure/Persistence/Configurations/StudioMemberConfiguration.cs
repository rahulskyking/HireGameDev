using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HireGameDev.Infrastructure.Persistence.Configurations;

internal sealed class StudioMemberConfiguration : IEntityTypeConfiguration<StudioMember>
{
    public void Configure(EntityTypeBuilder<StudioMember> builder)
    {
        builder.ToTable("studio_members", t =>
        {
            t.HasCheckConstraint(
                "ck_studio_members_member_role",
                "member_role IN ('owner', 'recruiter', 'viewer')");
        });

        builder.HasKey(m => new { m.StudioId, m.UserId });

        builder.Property(m => m.MemberRole)
            .HasConversion(new SnakeCaseEnumValueConverter<StudioMemberRole>())
            .HasColumnType("text")
            .IsRequired();

        builder.Property(m => m.InvitedAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(m => m.AcceptedAt)
            .HasColumnType("timestamptz");

        builder.HasOne(m => m.Studio)
            .WithMany(s => s.Members)
            .HasForeignKey(m => m.StudioId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => m.UserId)
            .HasDatabaseName("ix_studio_members_user_id");
    }
}
