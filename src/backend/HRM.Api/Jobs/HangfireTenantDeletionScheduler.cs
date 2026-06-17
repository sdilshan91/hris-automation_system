using HRM.Application.Common.Interfaces;
using Hangfire;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire-backed <see cref="ITenantDeletionScheduler"/> (US-ADM-004 FR-2/FR-6). Schedules the tenant-aware
/// <see cref="TenantDeletionJob"/> at <c>TerminationScheduledAt</c> plus the 14d/7d/1d
/// <see cref="TenantTerminationReminderJob"/> reminders, and cancels them on Restore. Lives in HRM.Api
/// alongside the jobs + Hangfire JobStorage; bound to the interface so the Infrastructure lifecycle service can
/// enqueue by interface (mirrors <c>HangfireOfferExpiryScheduler</c>). When the lifecycle service runs without
/// this registration (tests/dev) it simply skips scheduling.
/// </summary>
public sealed class HangfireTenantDeletionScheduler : ITenantDeletionScheduler
{
    private static readonly int[] ReminderDaysBefore = { 14, 7, 1 };

    private readonly IBackgroundJobClient _backgroundJobs;

    public HangfireTenantDeletionScheduler(IBackgroundJobClient backgroundJobs)
    {
        _backgroundJobs = backgroundJobs;
    }

    public IReadOnlyList<ScheduledTenantJob> ScheduleDeletionAndReminders(Guid tenantId, DateTime deletionFireAtUtc)
    {
        var jobs = new List<ScheduledTenantJob>();
        var now = DateTime.UtcNow;

        // The deletion job at the grace boundary.
        if (deletionFireAtUtc > now)
        {
            var deletionJobId = _backgroundJobs.Schedule<TenantDeletionJob>(
                job => job.RunAsync(tenantId), deletionFireAtUtc - now);
            jobs.Add(new ScheduledTenantJob(deletionJobId, "deletion", deletionFireAtUtc));
        }

        // Reminder jobs at 14d/7d/1d before deletion (skip any whose fire-time is already in the past).
        foreach (var daysBefore in ReminderDaysBefore)
        {
            var fireAt = deletionFireAtUtc.AddDays(-daysBefore);
            if (fireAt <= now)
                continue;

            var reminderJobId = _backgroundJobs.Schedule<TenantTerminationReminderJob>(
                job => job.RunAsync(tenantId, daysBefore), fireAt - now);
            jobs.Add(new ScheduledTenantJob(reminderJobId, $"reminder_{daysBefore}d", fireAt));
        }

        return jobs;
    }

    public void Cancel(string? jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return;

        _backgroundJobs.Delete(jobId);
    }
}
