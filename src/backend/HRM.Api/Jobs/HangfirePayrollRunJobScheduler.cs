using HRM.Application.Common.Interfaces;
using Hangfire;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire-backed <see cref="IPayrollRunJobScheduler"/> (US-PAY-003 FR-2). Enqueues the tenant-aware
/// <see cref="ProcessPayrollRunJob"/> via <c>IBackgroundJobClient.Enqueue</c>. Lives in HRM.Api alongside the
/// job + Hangfire JobStorage; bound to the interface so the Infrastructure PayrollRunService can enqueue by
/// interface (mirrors HangfireInterviewReminderScheduler). When unregistered (tests/dev without Hangfire
/// storage) the run service skips enqueue and the processor is invoked directly.
/// </summary>
public sealed class HangfirePayrollRunJobScheduler : IPayrollRunJobScheduler
{
    private readonly IBackgroundJobClient _backgroundJobs;

    public HangfirePayrollRunJobScheduler(IBackgroundJobClient backgroundJobs)
    {
        _backgroundJobs = backgroundJobs;
    }

    public string Enqueue(Guid tenantId, string tenantSubdomain, Guid runId)
        => _backgroundJobs.Enqueue<ProcessPayrollRunJob>(job => job.RunAsync(tenantId, tenantSubdomain, runId));
}
