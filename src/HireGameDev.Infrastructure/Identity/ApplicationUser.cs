using HireGameDev.Domain.Common;
using HireGameDev.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace HireGameDev.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity user mapped to the `users` table.
/// Note: Candidate phone numbers live on `candidate_profiles.phone` (Phase 1), not on `users`.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>, IAuditableEntity, ISoftDeletable
{
    public AccountType AccountType { get; set; } = AccountType.Candidate;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public ThemePreference ThemePreference { get; set; } = ThemePreference.System;

    public string Timezone { get; set; } = "Asia/Kolkata";

    public DateTimeOffset? AgeConfirmedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}
