using HireGameDev.Application.Features.Audit;

namespace HireGameDev.Application.Abstractions.Audit;

public interface IAuditLogger
{
    void Enqueue(AuditLogEntry entry);

    Task LogAsync(
        AuditLogEntry entry,
        CancellationToken cancellationToken = default);
}
