namespace HRM.Application.Common.Interfaces;

/// <summary>
/// 360-degree reviewer-reminder service (US-PRF-005 AC-5/FR-8). Invoked by the Hangfire recurring job once
/// per active tenant (the job sets the tenant context, then calls this). For the current tenant it finds
/// reviewer assignments that are still Pending in any Active 360-enabled cycle whose manager-review window is
/// open, and dispatches a reminder via the existing <see cref="IPerformanceNotificationService"/> seam.
/// Tenant-scoped via the EF global query filter, so it never crosses tenants (NFR-2).
/// </summary>
public interface IFeedback360ReminderService
{
    /// <summary>
    /// Sends reminders for the CURRENT tenant context. <paramref name="nowUtc"/> is injected for testability.
    /// Returns the number of reminders dispatched.
    /// </summary>
    Task<int> SendDueRemindersAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
