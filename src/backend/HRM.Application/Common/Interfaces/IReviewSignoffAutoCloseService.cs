namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Auto-close service for unsigned performance reviews (US-PRF-006 BR-3). Invoked once per active tenant by
/// the Hangfire recurring job (which sets the tenant context first). For the current tenant it finds reviews
/// still awaiting employee sign-off (PendingEmployeeSignOff) whose request is older than the cycle's
/// tenant-configurable window (default 7 days), flips them to NoResponse, records an immutable
/// AutoClosedNoResponse sign-off, and notifies HR. Tenant-scoped via the EF global query filter (NFR-2).
/// Mirrors <see cref="IFeedback360ReminderService"/> / <see cref="ISelfAssessmentReminderService"/>.
/// </summary>
public interface IReviewSignoffAutoCloseService
{
    /// <summary>
    /// Auto-closes overdue unsigned reviews for the CURRENT tenant context. <paramref name="nowUtc"/> is
    /// injected for testability. Returns the number of reviews auto-closed.
    /// </summary>
    Task<int> AutoCloseOverdueAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
