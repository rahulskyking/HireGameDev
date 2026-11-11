using HireGameDev.Domain.Common;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Entities;

public sealed class StudioProfile : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }

    public Guid OwnerUserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Normalized relative object key under <c>StorageVisibility.Public</c>
    /// (for example, <c>logos/4c2b...a9.webp</c>).
    /// </summary>
    public string? LogoPath { get; set; }

    public string? Website { get; set; }

    public string? Description { get; set; }

    public int? CityId { get; set; }

    public City? City { get; set; }

    public StudioSizeBand SizeBand { get; set; } = StudioSizeBand.Band1To10;

    public StudioType StudioType { get; set; } = StudioType.Indie;

    public short? FoundedYear { get; set; }

    public string[] CultureTags { get; set; } = [];

    public string? Gstin { get; set; }

    public string? CompanyEmailDomain { get; set; }

    public StudioVerificationStatus VerificationStatus { get; set; } = StudioVerificationStatus.Unverified;

    public DateTimeOffset? VerificationSubmittedAt { get; set; }

    public string[] VerificationProofUrls { get; set; } = [];

    public string? VerificationApplicantNotes { get; set; }

    public string? PendingIdentityName { get; set; }

    public string? PendingIdentitySlug { get; set; }

    public string? PendingIdentityGstin { get; set; }

    public DateTimeOffset? PendingIdentityRequestedAt { get; set; }

    public DateTimeOffset? VerifiedAt { get; set; }

    public Guid? VerifiedBy { get; set; }

    public string? VerificationNotes { get; set; }

    public int Apps90d { get; set; }

    public decimal? ResponseRate90d { get; set; }

    public decimal? MedianFirstResponseHours { get; set; }

    public DateTimeOffset? MetricsUpdatedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<StudioMember> Members { get; set; } = new List<StudioMember>();

    public ICollection<StudioTitle> Titles { get; set; } = new List<StudioTitle>();

    public ICollection<StudioSkill> StudioSkills { get; set; } = new List<StudioSkill>();
}
