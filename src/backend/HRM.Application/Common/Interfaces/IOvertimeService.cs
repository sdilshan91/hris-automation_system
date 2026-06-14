using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Overtime tracking and approval (US-ATT-006). All operations are tenant-scoped via ITenantContext +
/// the EF global query filters (NFR-2 asks for PostgreSQL RLS, which this codebase does NOT use —
/// tenant isolation is via EF filters + TenantInterceptor, consistent with the rest of Attendance).
///
/// Auto-detection on clock-out is NOT here — per NFR-1 it runs inside the clock-out transaction
/// (AttendanceService.ClockOutAsync) via the shared OvertimeDetection domain helper. This service owns
/// the employee pre-approval submit, the employee's own list, the manager queue, approve/reject, and
/// the HR monthly report.
///
/// Deferred / flagged dependencies (do NOT block — owned by their stories):
///   - Approval Workflow Engine (FR-5 pre-approval routing / multi-level): NONE exists (US-ADM-007).
///     Pre-approval is a simple submitted PENDING request; approval is SINGLE-LEVEL direct-report
///     (mirrors US-ATT-004). workflow_instance_id stays null.
///   - Notifications (FR-8 HR alert + manager/employee notifications): no infra — DEFERRED (TODO US-NTF).
///   - Payroll (FR-7 / US-ATT-009): approval sets IsPayrollReady; no payroll integration is built.
/// </summary>
public interface IOvertimeService
{
    /// <summary>
    /// AC-1/FR-1/NFR-1: builds (but does NOT save) an <see cref="OvertimeRecord"/> for a just-closed
    /// attendance session when net work exceeds the shift standard by at least the threshold (BR-1/BR-2).
    /// Returns null when no overtime is recognized. The caller (clock-out) adds it to the SAME
    /// change-tracker and commits it atomically with the attendance_log — there is no separate API call
    /// or SaveChanges. Applies the daily cap (BR-4), the weekday/weekend/holiday multiplier (BR-3/BR-7),
    /// the weekly-cap alert flag (BR-5), and UNAPPROVED status when pre-approval is required but missing
    /// (BR-6). <paramref name="netWorkMinutes"/> is the calculator's post-break TotalWorkMinutes.
    /// </summary>
    Task<OvertimeRecord?> BuildAutoDetectedAsync(
        AttendanceLog log,
        Employee employee,
        AttendanceSettings settings,
        int netWorkMinutes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-2/FR-4: the acting employee submits an overtime pre-approval request (a PRE_APPROVED,
    /// PENDING record). Returns 400 (tenant unresolved / past or invalid date / invalid hours),
    /// 403 (no employee linked / inactive).
    /// </summary>
    Task<Result<OvertimeDto>> SubmitPreApprovalAsync(
        OvertimePreApprovalRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the acting employee's own overtime records, newest date first. 400 tenant unresolved;
    /// 403 no employee linked.
    /// </summary>
    Task<Result<IReadOnlyList<OvertimeDto>>> GetMyOvertimeAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-3/FR-5: the acting manager's pending overtime queue — PENDING records from their direct
    /// reports with employee name/photo and submitted-on. Empty (not an error) when no employee is
    /// linked or no direct reports. 400 tenant unresolved.
    /// </summary>
    Task<Result<OvertimeQueueResult>> GetPendingForManagerAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-4/FR-6/FR-7: the manager approves an overtime record from a direct report. Sets status
    /// APPROVED, approved_minutes (defaults to overtime_minutes, or the adjusted value capped at it),
    /// flags it payroll-ready, and writes an immutable history row — atomically (NFR-3). BR-8 blocks
    /// self-approval (403 "self_approval"). Returns 400 (tenant unresolved / approvedMinutes out of
    /// range), 403 ("self_approval" / "not_team_member"), 404 (not found), 409 ("already_actioned").
    /// </summary>
    Task<Result<OvertimeDecisionDto>> ApproveAsync(
        Guid overtimeId, int? approvedMinutes, string? comment,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The manager rejects an overtime record from a direct report (mandatory reason, min 10 chars).
    /// Same authorization/immutability/self-approval rules as approve.
    /// </summary>
    Task<Result<OvertimeDecisionDto>> RejectAsync(
        Guid overtimeId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-5: the monthly HR overtime report — per-employee approved/pending/rejected minutes for the
    /// given month plus tenant-wide totals. 400 tenant unresolved.
    /// </summary>
    Task<Result<OvertimeReportResult>> GetMonthlyReportAsync(
        int year, int month, CancellationToken cancellationToken = default);
}
