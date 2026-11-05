using System.Net;
using HireGameDev.Application.Features.Consent;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Abstractions.Consent;

public interface IConsentService
{
    Task RecordConsentAsync(
        Guid userId,
        ConsentType consentType,
        string version,
        IPAddress? ip = null,
        CancellationToken cancellationToken = default);

    Task RecordRegistrationConsentsAsync(
        Guid userId,
        IPAddress? ip = null,
        CancellationToken cancellationToken = default);

    Task<bool> HasAcceptedVersionAsync(
        Guid userId,
        ConsentType consentType,
        string requiredVersion,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConsentRecordItem>> GetUserConsentsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
