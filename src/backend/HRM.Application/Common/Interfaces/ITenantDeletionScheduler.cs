namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Schedules / cancels the tenant data-deletion job and the pre-deletion reminder jobs for a termination grace
/// period (US-ADM-004 FR-2/FR-6).
///
/// SEAM: mirrors <see cref="IOfferExpiryScheduler"/> / <see cref="IInterviewReminderScheduler"/>. The concrete
/// Hangfire-backed implementation lives in HRM.Api (where the jobs + JobStorage live), bound to this interface
/// so the Infrastructure lifecycle service can enqueue by interface. It is registered as OPTIONAL on the
/// service (nullable ctor param): when absent (unit/integration tests, or before Hangfire storage is
/// initialised) the service simply skips scheduling and records no job ids, so termination never requires real
/// Hangfire storage to run.
/// </summary>
public interface ITenantDeletionScheduler
{
    /// <summary>
    /// Schedules the data-deletion job to fire at <paramref name="deletionFireAtUtc"/> plus reminder jobs at
    /// 14d/7d/1d before it (skipping any reminder whose fire-time is already in the past). Returns the created
    /// Hangfire job ids (each tagged with its type) so the caller can persist them for later cancellation.
    /// </summary>
    IReadOnlyList<ScheduledTenantJob> ScheduleDeletionAndReminders(Guid tenantId, DateTime deletionFireAtUtc);

    /// <summary>Cancels a previously scheduled job (deletion or reminder). No-op for null/unknown ids.</summary>
    void Cancel(string? jobId);
}

/// <summary>A scheduled Hangfire job for a terminating tenant (US-ADM-004): its id, type, and fire-time.</summary>
public sealed record ScheduledTenantJob(string JobId, string JobType, DateTime ScheduledFor);
