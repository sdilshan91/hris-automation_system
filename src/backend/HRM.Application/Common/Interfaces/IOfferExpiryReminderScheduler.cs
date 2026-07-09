namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Schedules / cancels the offer-expiry-<em>reminder</em> background job (US-REC-007 FR-7/AC-4). This is the
/// "N days before expiry" nudge to the candidate (+ recruiter pool), SEPARATE from
/// <see cref="IOfferExpiryScheduler"/> (the auto-expire at the expiry boundary).
///
/// SEAM: mirrors <see cref="IOfferExpiryScheduler"/>. The concrete Hangfire-backed implementation lives in
/// HRM.Api (where the job + JobStorage live), bound to this interface so the Infrastructure offer service can
/// enqueue by interface. Registered as OPTIONAL on the offer service (nullable ctor param): when absent
/// (unit/integration tests, or before Hangfire storage is initialised) the service simply skips scheduling
/// and stores no job id, so the send flow never requires real Hangfire storage to run.
/// </summary>
public interface IOfferExpiryReminderScheduler
{
    /// <summary>
    /// Schedules a tenant-aware expiry-reminder job to fire at <paramref name="fireAtUtc"/> (FR-7/AC-4).
    /// Returns the created Hangfire job id (stored on the offer for later cancel). If the fire-time is
    /// already in the past (offer sent close to expiry), the job is scheduled to run immediately — its
    /// IsActive guard keeps that safe — rather than skipped.
    /// </summary>
    string? Schedule(Guid tenantId, Guid offerId, DateTime fireAtUtc);

    /// <summary>Cancels a previously scheduled reminder job (on response/withdraw/supersede). No-op for null/unknown ids.</summary>
    void Cancel(string? jobId);
}
