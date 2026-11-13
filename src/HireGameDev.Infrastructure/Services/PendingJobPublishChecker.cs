using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Application.Features.Jobs;

namespace HireGameDev.Infrastructure.Services;

/// <summary>Deliberately fail closed until M2.1c installs the deterministic publish checker.</summary>
internal sealed class PendingJobPublishChecker : IJobPublishChecker
{
    public Task<JobPublishCheckResult> CheckAsync(JobPublishContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(new JobPublishCheckResult(new[]
        {
            new JobPublishFinding("Jobs.PublishChecksUnavailable", JobFindingSeverity.BlockingError, null,
                "Publishing is not enabled until mandatory quality and anti-spam checks are available.")
        }));
}
