using HRM.Domain.Enums;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Dispatches Core-HR notifications (US-NTF-006 Phase 7 — the final inline "deferred notify" sites in
/// US-CHR-008/009/011). One method per trigger site. Every method is fire-and-forget-safe: it is awaited by the
/// caller but MUST never throw into the caller's path — the underlying write / scan is already committed, so a
/// delivery failure must not break it.
///
/// <para>The default implementation (<c>RealCoreHrNotificationService</c>) resolves recipients from the DB and
/// dispatches in-app + email via <c>INotificationDispatcher</c>. A <c>LogOnlyCoreHrNotificationService</c> sibling
/// is retained for tests. Resolution queries use <c>IgnoreQueryFilters</c> scoped to an EXPLICIT tenant id so they
/// are correct outside a request scope (the probation + document jobs run under the system context).</para>
/// </summary>
public interface ICoreHrNotificationService
{
    /// <summary>
    /// Reminds the HR pool that an employee's probation is ending soon (US-CHR-009 FR-6/BR-6). Cross-tenant job
    /// caller: the tenant id is passed EXPLICITLY because the caller runs under the system context scanning every
    /// tenant. Recipient: the HR pool of <paramref name="tenantId"/> (users holding <c>Employee.ChangeStatus</c>).
    /// </summary>
    Task NotifyProbationEndingAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly probationEndDate,
        int daysRemaining,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Alerts the HR pool that a terminated/suspended manager has direct reports needing reassignment
    /// (US-CHR-011 BR-4). The tenant is resolved from the manager's OWN record, so it is correct whether the caller
    /// runs request-scoped or under the system-context future-dated job. Recipient: the HR pool.
    /// </summary>
    Task NotifyManagerReassignmentNeededAsync(
        Guid managerEmployeeId,
        EmployeeStatus newStatus,
        int directReportCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Warns the document-owner employee that one of their documents expires soon (US-CHR-008 FR-8/BR-4).
    /// Cross-tenant job caller: the tenant id is passed EXPLICITLY. Recipient: the owner employee (in-app + email,
    /// or email-only when they have no linked user).
    /// </summary>
    Task NotifyDocumentExpiryAsync(
        Guid tenantId,
        Guid employeeId,
        string fileName,
        string category,
        DateOnly expiryDate,
        int daysUntilExpiry,
        CancellationToken cancellationToken = default);
}
