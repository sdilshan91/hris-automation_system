using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Manager approve/reject of attendance regularization requests (US-ATT-004). All operations are
/// tenant-scoped via ITenantContext + the EF global query filters; the acting manager is resolved from
/// the current user and may only act on requests from their direct reports (FR-7/AC-5).
///
/// Single-level approval only: an approve is the FINAL approve and immediately mutates the
/// attendance_log (FR-2). Multi-level routing (FR-4/AC-4) needs an Approval Workflow Engine that does
/// not exist yet (US-ADM-007) — flagged, not faked. Notifications (FR-5) and Redis (FR-8) are
/// deferred (no infra). Tenant isolation uses EF global filters, not PostgreSQL RLS (NFR-3).
/// </summary>
public interface IRegularizationApprovalService
{
    /// <summary>
    /// Returns the pending regularization queue for the acting manager — pending requests from their
    /// direct reports (FR-1/AC-3), with employee name, date, requested times, reason, and submitted-on.
    /// Supports optional employee/date filtering. Fails with 400 when the tenant is unresolved; returns
    /// an empty queue (not an error) when no employee record is linked to the user.
    /// </summary>
    Task<Result<PendingRegularizationQueueResult>> GetPendingForManagerAsync(
        PendingRegularizationQueryParams queryParams,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a pending regularization (US-ATT-004 AC-1/FR-2, BR-3/BR-5/BR-6/BR-7). As the final
    /// (single-level) approver: sets status APPROVED, creates or updates the attendance_log for the
    /// employee+date with the regularized times, recalculates total_work_minutes/overtime/status via
    /// the shared AttendanceCalculator, and writes an immutable decision-history row — all in one
    /// atomic SaveChanges (NFR-2). Returns (failures carry a stable ErrorCode):
    ///   400 when the tenant is unresolved, or the request is missing both requested times to build a log;
    ///   403 when no employee is linked to the user, the manager is not the request owner's manager
    ///       (FR-7/AC-5), or it is the manager's OWN request (BR-6, code "self_approval");
    ///   404 when the regularization does not exist (in the manager's tenant);
    ///   409 when the request is already actioned (BR-3, code "already_actioned") or its date now falls
    ///       in a locked payroll period (BR-5, code "payroll_period_locked").
    /// </summary>
    Task<Result<RegularizationDecisionDto>> ApproveAsync(
        Guid regularizationId,
        string? comment,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects a pending regularization (US-ATT-004 AC-2/FR-3, BR-1/BR-3/BR-6). Sets status REJECTED and
    /// records the mandatory reason (min 10 chars) in an immutable decision-history row; the
    /// attendance_log is NOT touched. Same authorization/immutability/self-approval rules as approve.
    /// Returns 400 (tenant unresolved / blank reason), 403, 404, 409 as for approve.
    /// </summary>
    Task<Result<RegularizationDecisionDto>> RejectAsync(
        Guid regularizationId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk-approves multiple regularizations in one action (US-ATT-004 BR-7). Each id runs the full
    /// per-item authorization + immutability + payroll-lock pipeline independently; per-item results are
    /// reported so the caller can surface a partial-success summary. Each successful item is committed
    /// in its own atomic SaveChanges (a blocked item never rolls back an already-approved one). Fails
    /// wholesale with 400 only when the tenant is unresolved or the id list is empty; 403 when no
    /// employee is linked to the user.
    /// </summary>
    Task<Result<BulkApproveRegularizationResult>> BulkApproveAsync(
        IReadOnlyList<Guid> regularizationIds,
        string? comment,
        CancellationToken cancellationToken = default);
}
