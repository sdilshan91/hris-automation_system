using HRM.Application.Common.Interfaces;
using Hangfire;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire-backed <see cref="IOfferExpiryReminderScheduler"/> (US-REC-007 FR-7/AC-4). Schedules the
/// tenant-aware <see cref="OfferExpiryReminderJob"/> via <c>IBackgroundJobClient.Schedule</c> at the computed
/// "N days before expiry" fire-time, and cancels it via <c>Delete</c> on response/withdraw/supersede. Lives in
/// HRM.Api alongside the job + Hangfire JobStorage; bound to the interface so the Infrastructure offer service
/// can enqueue by interface (mirrors <see cref="HangfireOfferExpiryScheduler"/>). When the offer service runs
/// without this registration (tests/dev) it simply skips scheduling.
/// </summary>
public sealed class HangfireOfferExpiryReminderScheduler : IOfferExpiryReminderScheduler
{
    private readonly IBackgroundJobClient _backgroundJobs;

    public HangfireOfferExpiryReminderScheduler(IBackgroundJobClient backgroundJobs)
    {
        _backgroundJobs = backgroundJobs;
    }

    public string? Schedule(Guid tenantId, Guid offerId, DateTime fireAtUtc)
    {
        // If the fire-time is already past (offer sent close to expiry), enqueue immediately rather than
        // skip — Hangfire runs a past-dated Schedule right away and the job's IsActive guard keeps it safe.
        var delay = fireAtUtc <= DateTime.UtcNow ? TimeSpan.Zero : fireAtUtc - DateTime.UtcNow;
        return _backgroundJobs.Schedule<OfferExpiryReminderJob>(
            job => job.RunAsync(tenantId, offerId), delay);
    }

    public void Cancel(string? jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return;

        _backgroundJobs.Delete(jobId);
    }
}
