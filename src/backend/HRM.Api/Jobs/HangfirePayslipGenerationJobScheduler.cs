using HRM.Application.Common.Interfaces;
using Hangfire;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire-backed <see cref="IPayslipGenerationJobScheduler"/> (US-PAY-004 FR-4). Enqueues the tenant-aware
/// <see cref="GeneratePayslipsJob"/> via <c>IBackgroundJobClient.Enqueue</c>. Lives in HRM.Api alongside the
/// job + Hangfire JobStorage; bound to the interface so the Infrastructure PayslipGenerationService can
/// enqueue by interface (mirrors HangfirePayrollRunJobScheduler). When unregistered (tests/dev without
/// Hangfire storage) the generation service skips enqueue and the batch renderer is invoked directly.
/// </summary>
public sealed class HangfirePayslipGenerationJobScheduler : IPayslipGenerationJobScheduler
{
    private readonly IBackgroundJobClient _backgroundJobs;

    public HangfirePayslipGenerationJobScheduler(IBackgroundJobClient backgroundJobs)
    {
        _backgroundJobs = backgroundJobs;
    }

    public string Enqueue(Guid tenantId, string tenantSubdomain, Guid runId)
        => _backgroundJobs.Enqueue<GeneratePayslipsJob>(job => job.RunAsync(tenantId, tenantSubdomain, runId, CancellationToken.None));
}
