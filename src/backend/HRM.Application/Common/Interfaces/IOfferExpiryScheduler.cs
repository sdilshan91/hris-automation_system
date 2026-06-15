namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Schedules / cancels the offer-expiry follow-up background job (US-REC-007 FR-7/AC-4).
///
/// SEAM: mirrors <see cref="IInterviewReminderScheduler"/>. The concrete Hangfire-backed implementation
/// lives in HRM.Api (where the job + JobStorage live), bound to this interface so the Infrastructure offer
/// service can enqueue by interface. The implementation is registered as OPTIONAL on the offer service
/// (nullable ctor param): when absent (unit/integration tests, or before Hangfire storage is initialised)
/// the service simply skips scheduling and stores no job id, so the send flow never requires real Hangfire
/// storage to run.
/// </summary>
public interface IOfferExpiryScheduler
{
    /// <summary>
    /// Schedules a tenant-aware expiry job to fire at <paramref name="fireAtUtc"/> (FR-7/AC-4). Returns the
    /// created Hangfire job id (stored on the offer for later cancel), or null if the fire-time is already
    /// in the past (no job needed). Idempotent at the call site: the caller cancels any prior job first.
    /// </summary>
    string? Schedule(Guid tenantId, Guid offerId, DateTime fireAtUtc);

    /// <summary>Cancels a previously scheduled expiry job (on response/withdraw). No-op for null/unknown ids.</summary>
    void Cancel(string? jobId);
}
