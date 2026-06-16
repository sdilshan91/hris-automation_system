namespace HRM.Application.Common.Interfaces;

/// <summary>
/// PIP reminder/sweep service (US-PRF-008 FR-3/BR-4). Invoked once per active tenant by the Hangfire recurring
/// job (which sets the tenant context first). For the CURRENT tenant it dispatches, via the performance
/// notification seam:
///   - checkpoint reminders 3 days before each upcoming checkpoint date (FR-3),
///   - PIP end-date reminders (3 days before the end date, FR-3),
///   - overdue-checkpoint alerts when a scheduled checkpoint date has passed with no checkpoint recorded (FR-3),
/// and applies the BR-4 "Not Acknowledged" flag to any Active PIP the employee did not acknowledge within
/// 5 business days of initiation (writing an immutable NotAcknowledged event). Every query rides the EF global
/// query filter, so the sweep never crosses tenants (NFR-2). Mirrors <see cref="IReviewSignoffAutoCloseService"/>.
/// </summary>
public interface IPipReminderService
{
    /// <summary>
    /// Runs the PIP reminder + not-acknowledged sweep for the CURRENT tenant context. <paramref name="nowUtc"/>
    /// is injected for testability. Returns the number of notifications dispatched + PIPs flagged.
    /// </summary>
    Task<int> RunSweepAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
