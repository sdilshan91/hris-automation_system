namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Schedules / cancels the pre-interview reminder background job (US-REC-005 FR-4/BR-6/NFR-3/NFR-4).
///
/// SEAM: the concrete Hangfire-backed implementation lives in HRM.Api (where the job + JobStorage live),
/// bound to this interface so the Infrastructure interview service can enqueue by interface — mirrors the
/// <see cref="ILeaveReportExportJob"/> pattern. The implementation is registered as OPTIONAL on the
/// interview service (nullable ctor param): when absent (unit/integration tests, or before Hangfire
/// storage is initialised) the service simply skips scheduling and stores no job id, so the scheduling
/// flow never requires real Hangfire storage to run.
/// </summary>
public interface IInterviewReminderScheduler
{
    /// <summary>
    /// Schedules a tenant-aware reminder job to fire at <paramref name="fireAtUtc"/> (FR-4/NFR-4). Returns
    /// the created Hangfire job id (stored on the interview for later cancel/swap), or null if the
    /// fire-time is already in the past (no reminder needed). Idempotent at the call site: the caller
    /// deletes any prior job before scheduling a replacement (BR-6).
    /// </summary>
    string? Schedule(Guid tenantId, Guid interviewId, DateTime fireAtUtc);

    /// <summary>Cancels a previously scheduled reminder job (FR-4/BR-6 — on reschedule/cancel). No-op for null/unknown ids.</summary>
    void Cancel(string? jobId);
}
