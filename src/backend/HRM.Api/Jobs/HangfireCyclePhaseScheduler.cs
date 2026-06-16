using HRM.Application.Common.Interfaces;
using Hangfire;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire-backed <see cref="ICyclePhaseScheduler"/> (US-PRF-004 FR-5/AC-2/AC-5/NFR-3). Registers a daily
/// recurring job per cycle (id "cycle-phase-{cycleId}") that runs the tenant-aware
/// <see cref="CyclePhaseTransitionJob"/> to dispatch phase-start / deadline-reminder / phase-close /
/// overdue-escalation notifications. A daily recurring schedule (rather than one job per fire-time) keeps the
/// schedule trivially idempotent across edits (AC-5): re-registering the same job id replaces it; cancelling
/// removes it. Lives in HRM.Api alongside the job + Hangfire JobStorage; bound to the interface so the
/// Infrastructure cycle service can schedule by interface. Absent in tests ⇒ the service skips scheduling.
/// </summary>
public sealed class HangfireCyclePhaseScheduler : ICyclePhaseScheduler
{
    private readonly IRecurringJobManager _recurringJobs;

    public HangfireCyclePhaseScheduler(IRecurringJobManager recurringJobs) => _recurringJobs = recurringJobs;

    private static string JobId(Guid cycleId) => $"cycle-phase-{cycleId}";

    public void ScheduleCycleJobs(Guid tenantId, Guid cycleId)
    {
        // Daily at 08:00 UTC; the job itself decides which (if any) notification thresholds fire today.
        _recurringJobs.AddOrUpdate<CyclePhaseTransitionJob>(
            JobId(cycleId),
            job => job.RunAsync(tenantId, cycleId),
            "0 8 * * *");
    }

    public void CancelCycleJobs(Guid cycleId) => _recurringJobs.RemoveIfExists(JobId(cycleId));
}
