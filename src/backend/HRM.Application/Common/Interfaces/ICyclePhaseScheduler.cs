namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Schedules / cancels the per-cycle phase-transition background jobs (US-PRF-004 FR-5/AC-2/AC-4/NFR-3):
/// phase-start notifications, deadline reminders, phase-close notifications, and overdue-escalation alerts.
///
/// SEAM: the concrete Hangfire-backed implementation lives in HRM.Api (where the jobs + JobStorage live),
/// bound to this interface so the Infrastructure cycle service can enqueue by interface — mirrors
/// <see cref="IInterviewReminderScheduler"/>. The implementation is OPTIONAL on the cycle service (nullable
/// ctor param): when absent (unit/integration tests, or before Hangfire storage is initialised) the service
/// records the intent in its log and skips real scheduling, so cycle creation never requires Hangfire to run.
/// </summary>
public interface ICyclePhaseScheduler
{
    /// <summary>
    /// (Re)schedules all phase-transition + reminder jobs for a cycle (FR-5). Idempotent: any previously
    /// scheduled jobs for this cycle are cancelled first, so calling on edit/extend (AC-5) cleanly replaces
    /// the schedule. Jobs are tenant-aware (NFR-3) — they set the tenant context before dispatching via the
    /// existing IPerformanceNotificationService seam. A no-op for phases whose fire-time is already past.
    /// </summary>
    void ScheduleCycleJobs(Guid tenantId, Guid cycleId);

    /// <summary>Cancels all scheduled jobs for a cycle (FR-7 — on cancel/complete/pause/delete). No-op if none.</summary>
    void CancelCycleJobs(Guid cycleId);
}
