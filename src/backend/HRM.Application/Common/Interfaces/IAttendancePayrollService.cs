using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Attendance ⇄ Payroll integration — ATTENDANCE SIDE (US-ATT-009). Exposes the per-employee
/// attendance data the (not-yet-built) Payroll module consumes (FR-1/FR-2/FR-7/FR-8), the
/// attendance-period lock lifecycle (FR-3/FR-4, AC-4/AC-5), and the attendance-side reconciliation
/// view (FR-5). All salary math (LOP deduction AC-2, overtime pay AC-3) belongs to the Payroll
/// module and is intentionally NOT implemented here.
///
/// Reuses <see cref="IAttendanceSummaryService"/> for the core monthly rollup (present/absent/lop/
/// work/overtime). Tenant-scoped via ITenantContext + EF global query filters (no PostgreSQL RLS).
/// </summary>
public interface IAttendancePayrollService
{
    /// <summary>
    /// FR-1/FR-2/FR-7/FR-8/NFR-1: per-employee attendance data for a period, optionally filtered to a
    /// specific set of employee ids. Reuses the materialized monthly summary for present/absent/lop/
    /// work/overtime and augments it with working-days, the late-deduction breakdown, and the
    /// approved-overtime multiplier breakdown. BR-7: terminated employees are included up to their
    /// last working day.
    /// </summary>
    Task<Result<AttendancePayrollResult>> GetPayrollDataAsync(
        int year, int month, IReadOnlyList<Guid>? employeeIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// §7: the current ACTIVE lock covering the given month (for the banner/status), or null when the
    /// month is not locked.
    /// </summary>
    Task<Result<PeriodLockDto?>> GetPeriodLockAsync(
        int year, int month, CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-4/FR-3/FR-4: locks an attendance period. Atomic (NFR-2); records the acting HR user + UTC
    /// timestamp in-row and via the AuditInterceptor. Rejects an invalid range or a period that is
    /// already actively locked.
    /// </summary>
    Task<Result<PeriodLockDto>> LockPeriodAsync(
        DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-5/FR-4: unlocks a previously-locked period so HR can correct an error, recording the acting
    /// user + UTC timestamp (FR-6 payroll-refresh trigger is DEFERRED — no Payroll module to notify;
    /// a re-pull of GetPayrollDataAsync always returns current data).
    /// </summary>
    Task<Result<PeriodLockDto>> UnlockPeriodAsync(
        Guid lockId, CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-5 (attendance side): the reconciliation rows for a period (present/lop/overtime/work). The
    /// side-by-side payroll-input columns are DEFERRED until the Payroll module exists.
    /// </summary>
    Task<Result<ReconciliationResult>> GetReconciliationAsync(
        int year, int month, CancellationToken cancellationToken = default);
}
