using HRM.Application.Common.Interfaces;
using Hangfire;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire-backed <see cref="IPayslipDistributionJobScheduler"/> (US-PAY-011 FR-1/FR-8). Enqueues the
/// tenant-aware <see cref="SendPayslipEmailsJob"/> via <c>IBackgroundJobClient.Enqueue</c>. Lives in HRM.Api
/// alongside the job + Hangfire JobStorage; bound to the interface so the Infrastructure
/// PayslipDistributionService can enqueue by interface (mirrors HangfirePayslipGenerationJobScheduler). When
/// unregistered (tests/dev without Hangfire storage) the distribution service skips enqueue and the runner is
/// invoked directly.
/// </summary>
public sealed class HangfirePayslipDistributionJobScheduler : IPayslipDistributionJobScheduler
{
    private readonly IBackgroundJobClient _backgroundJobs;

    public HangfirePayslipDistributionJobScheduler(IBackgroundJobClient backgroundJobs)
    {
        _backgroundJobs = backgroundJobs;
    }

    public string Enqueue(Guid tenantId, string tenantSubdomain, Guid runId, IReadOnlyCollection<Guid>? targetEmployeeIds)
    {
        // Hangfire serializes the args; materialize the target set to a concrete List for portability.
        var targets = targetEmployeeIds is null ? null : new List<Guid>(targetEmployeeIds);
        return _backgroundJobs.Enqueue<SendPayslipEmailsJob>(
            job => job.RunAsync(tenantId, tenantSubdomain, runId, targets, CancellationToken.None));
    }
}
