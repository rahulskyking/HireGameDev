namespace HireGameDev.Application.Abstractions.Auth;

/// <summary>
/// Promotes confirmed user accounts matching configured <c>Admin:BootstrapEmails</c>
/// into the <c>admin</c> role idempotently with system audit logging (ADR-019).
/// </summary>
public interface IAdminBootstrapSeeder
{
    Task<int> SeedAsync(CancellationToken cancellationToken = default);

    Task<bool> EnsureBootstrapAdminForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
