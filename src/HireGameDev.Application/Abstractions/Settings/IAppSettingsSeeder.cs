namespace HireGameDev.Application.Abstractions.Settings;

public interface IAppSettingsSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
