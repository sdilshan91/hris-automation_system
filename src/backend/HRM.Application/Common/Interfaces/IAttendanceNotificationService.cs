namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Dispatches attendance-family notifications (US-NTF-006 Phase 6: US-ATT-001 FR-5, US-ATT-004 FR-4/FR-5,
/// US-ATT-006 FR-8). One method per trigger site. Every method is fire-and-forget-safe: it is awaited by the
/// caller but MUST never throw into the request path — the attendance/overtime/regularization write is already
/// committed, so a delivery failure must not break it.
///
/// <para>The default implementation (<c>RealAttendanceNotificationService</c>) resolves recipients from the DB
/// (the employee, the reporting-line manager, or the attendance-admin/HR pool) and dispatches in-app + email via
/// <c>INotificationDispatcher</c>. A <c>LogOnlyAttendanceNotificationService</c> sibling is retained for tests.</para>
/// </summary>
public interface IAttendanceNotificationService
{
    /// <summary>
    /// Notifies the employee that their clock-in was marked late (US-ATT-001 FR-5). Recipient: the employee.
    /// </summary>
    Task NotifyLateAsync(
        Guid employeeId,
        DateOnly date,
        TimeOnly checkIn,
        TimeOnly? expectedStart,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the employee's line manager that a regularization request was submitted (US-ATT-004 FR-4).
    /// Recipient: the employee's <c>ReportsToEmployeeId</c> manager.
    /// </summary>
    Task NotifyRegularizationRequestedAsync(
        Guid employeeId,
        DateOnly date,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the employee that their regularization request was approved or rejected (US-ATT-004 FR-5).
    /// Recipient: the employee. <paramref name="reason"/> is the original request reason on approve, or the
    /// manager's rejection reason on reject.
    /// </summary>
    Task NotifyRegularizationDecidedAsync(
        bool approved,
        Guid employeeId,
        DateOnly date,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Alerts the attendance-admin/HR pool that an employee's overtime exceeded a daily/weekly maximum
    /// (US-ATT-006 FR-8). Recipient: users holding the attendance-manage permission.
    /// </summary>
    Task NotifyOvertimeMaximaExceededAsync(
        Guid employeeId,
        decimal hours,
        decimal limit,
        string period,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the employee's manager that an overtime pre-approval is pending (US-ATT-006 FR-8).
    /// Recipient: the employee's line manager.
    /// </summary>
    Task NotifyOvertimePreApprovalRequestedAsync(
        Guid employeeId,
        DateOnly date,
        decimal hours,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the employee that their overtime was approved or rejected (US-ATT-006 FR-8). Recipient: the
    /// employee. <paramref name="reason"/> carries the rejection reason (null/ignored on approve).
    /// </summary>
    Task NotifyOvertimeDecidedAsync(
        bool approved,
        Guid employeeId,
        DateOnly date,
        decimal hours,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Escalates chronic lateness (US-ATT-008 FR-7): fired once, on the punch that first pushes the employee's
    /// distinct late-day count for the calendar month above the tenant chronic threshold. Recipients: BOTH the
    /// employee's line manager AND the attendance-admin/HR pool (de-duplicated). <paramref name="lateCount"/> is
    /// the month-to-date distinct late-day count, <paramref name="threshold"/> the tenant chronic threshold, and
    /// <paramref name="asOfLocalDate"/> the punch's tenant-local date (used for the month label).
    /// </summary>
    Task NotifyChronicLatenessAsync(
        Guid employeeId,
        int lateCount,
        int threshold,
        DateOnly asOfLocalDate,
        CancellationToken cancellationToken = default);
}
