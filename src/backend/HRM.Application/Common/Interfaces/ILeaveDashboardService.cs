using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Read/aggregation service for the employee Leave Balance Dashboard (US-LV-006).
/// All operations are tenant-scoped via ITenantContext + EF global query filters and resolve
/// the acting employee from the authenticated user (self-service; an employee can only ever
/// see their own data). Balances are computed component-wise from the LeaveLedger (BR-1);
/// the entitlement value is resolved by the US-LV-002 entitlement engine. No new persistence.
///
/// Redis balance caching (FR-5/NFR-1) is DEFERRED, consistent with the module-wide decision
/// across US-LV-001..005 — balances are computed from the ledger on every request.
/// </summary>
public interface ILeaveDashboardService
{
    /// <summary>
    /// Returns one balance summary per ACTIVE leave type for the current employee in the given
    /// leave year (FR-1/FR-2, AC-1). Deactivated types that still carry a non-zero balance are
    /// included with <c>IsArchived = true</c> (BR-3). When <paramref name="year"/> is null the
    /// current leave year is used (BR-4). If the employee has no ledger/entitlement data, returns
    /// an empty list (AC-5) — never throws.
    /// </summary>
    Task<Result<IReadOnlyList<LeaveBalanceDto>>> GetMyBalancesAsync(
        int? year,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current employee's full ledger transaction log for one leave type and year,
    /// ordered chronologically (FR-3, AC-2). Year defaults to the current leave year when null.
    /// </summary>
    Task<Result<IReadOnlyList<LeaveLedgerEntryDto>>> GetMyLedgerAsync(
        Guid leaveTypeId,
        int? year,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current employee's Approved + Pending future leave requests
    /// (start_date &gt;= today), ordered by start date (FR-4, AC-3).
    /// </summary>
    Task<Result<IReadOnlyList<UpcomingLeaveDto>>> GetMyUpcomingAsync(
        CancellationToken cancellationToken = default);
}
