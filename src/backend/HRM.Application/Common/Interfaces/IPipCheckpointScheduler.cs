namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Schedules the per-PIP checkpoint reminder background jobs (US-PRF-008 AC-2/FR-3).
///
/// SEAM: the actual reminder DELIVERY is driven by the daily tenant-wide <c>IPipReminderService</c> sweep
/// (checkpoint reminders 3 days out, end-date reminders, overdue alerts — same pattern as the review-signoff
/// auto-close sweep), so PIP creation does NOT require Hangfire to be running. This interface is an OPTIONAL
/// hook (nullable ctor param on PipService) for an environment that prefers to enqueue explicit one-shot
/// reminder jobs at creation time. When absent (unit/integration tests, or before Hangfire storage is
/// initialised) the service simply skips it — the daily sweep still covers FR-3. Mirrors
/// <see cref="ICyclePhaseScheduler"/>.
/// </summary>
public interface IPipCheckpointScheduler
{
    /// <summary>
    /// Records the checkpoint schedule for a PIP so reminders can fire 3 days before each date (FR-3). The
    /// default daily sweep already derives reminders from the persisted schedule; an implementation may
    /// additionally enqueue one-shot jobs. Idempotent. Tenant-aware (NFR-2).
    /// </summary>
    void ScheduleCheckpointReminders(Guid tenantId, Guid pipId, IReadOnlyList<DateOnly> checkpointDates);
}
