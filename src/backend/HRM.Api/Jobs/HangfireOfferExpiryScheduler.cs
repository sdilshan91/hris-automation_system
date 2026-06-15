using HRM.Application.Common.Interfaces;
using Hangfire;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire-backed <see cref="IOfferExpiryScheduler"/> (US-REC-007 FR-7/AC-4). Schedules the tenant-aware
/// <see cref="OfferExpiryJob"/> via <c>IBackgroundJobClient.Schedule</c> at the computed expiry fire-time,
/// and cancels it via <c>Delete</c> on response/withdraw. Lives in HRM.Api alongside the job + Hangfire
/// JobStorage; bound to the interface so the Infrastructure offer service can enqueue by interface (mirrors
/// the HangfireInterviewReminderScheduler seam). When the offer service runs without this registration
/// (tests/dev) it simply skips scheduling.
/// </summary>
public sealed class HangfireOfferExpiryScheduler : IOfferExpiryScheduler
{
    private readonly IBackgroundJobClient _backgroundJobs;

    public HangfireOfferExpiryScheduler(IBackgroundJobClient backgroundJobs)
    {
        _backgroundJobs = backgroundJobs;
    }

    public string? Schedule(Guid tenantId, Guid offerId, DateTime fireAtUtc)
    {
        // No job needed if the fire-time is already in the past (e.g. a same-day expiry). The offer is
        // still saved/sent.
        if (fireAtUtc <= DateTime.UtcNow)
            return null;

        var delay = fireAtUtc - DateTime.UtcNow;
        return _backgroundJobs.Schedule<OfferExpiryJob>(
            job => job.RunAsync(tenantId, offerId), delay);
    }

    public void Cancel(string? jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return;

        _backgroundJobs.Delete(jobId);
    }
}
