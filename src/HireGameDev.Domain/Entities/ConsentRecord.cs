using System.Net;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Entities;

/// <summary>
/// Versioned DPDP Act 2023 consent record in <c>consent_records</c>.
/// Retained as an append-only ledger so historical version acceptances remain auditable.
/// </summary>
public sealed class ConsentRecord
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public ConsentType ConsentType { get; set; }

    public string Version { get; set; } = string.Empty;

    public DateTimeOffset AcceptedAt { get; set; }

    public IPAddress? Ip { get; set; }
}
