namespace HireGameDev.Application.Abstractions.ReferenceData;

public interface IReferenceDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
