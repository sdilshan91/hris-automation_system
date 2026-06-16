namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Self-assessment deadline-reminder service (US-PRF-002 FR-7/AC-5). Invoked by the Hangfire recurring job
/// once per active tenant (the job sets the tenant context, then calls this). For the current tenant it
/// finds employees with assigned goals in any Active cycle whose self-assessment deadline is exactly one of
/// the configured day thresholds away (default 7/3/1) and who have NOT submitted, and dispatches a reminder
/// via the existing <see cref="IPerformanceNotificationService"/> seam. Tenant-scoped via the EF global
/// query filter, so it never crosses tenants (NFR-2).
/// </summary>
public interface ISelfAssessmentReminderService
{
    /// <summary>
    /// Sends reminders for the CURRENT tenant context. <paramref name="todayUtc"/> is injected for
    /// testability. Returns the number of reminders dispatched.
    /// </summary>
    Task<int> SendDueRemindersAsync(DateTime todayUtc, CancellationToken cancellationToken = default);

    /// <summary>Default day-before-deadline reminder thresholds (US-PRF-002 Assumptions §10): 7, 3, 1.</summary>
    static readonly IReadOnlyList<int> DefaultThresholds = new[] { 7, 3, 1 };
}
