using System.Net;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Features.Consent;

public sealed record ConsentRecordItem(
    long Id,
    Guid UserId,
    ConsentType ConsentType,
    string Version,
    DateTimeOffset AcceptedAt,
    IPAddress? Ip);
