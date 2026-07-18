using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Monthly attendance summary aggregation + read service (US-ATT-007). Computes per-employee monthly
/// rollups from the source attendance data (AttendanceLog / OvertimeRecord / AttendanceRegularization),
/// reconciled against approved leave and the resolved shift, and persists them to the
/// <c>attendance_monthly_summary</c> materialized table (which serves the FR-8 caching role — no Redis).
/// All operations are tenant-scoped via ITenantContext + the EF global query filters (NFR-3 RLS NOT
/// used). The Hangfire daily/monthly jobs and the on-demand path all call <see cref="GenerateAsync"/>.
/// </summary>
public interface IAttendanceSummaryService
{
    /// <summary>
    /// AC-1/AC-5/FR-5: reads the materialized monthly summary for a month, optionally filtered by
    /// department/location/shift/status. If no summary exists yet for the month it computes one
    /// on-demand first (AC-3). Returns rows + banner aggregates.
    /// </summary>
    Task<Result<MonthlySummaryResult>> GetMonthlyAsync(
        int year, int month, MonthlySummaryFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-2: day-by-day breakdown for one employee for a month (present/absent/leave/holiday/
    /// weekly-off/half-day, clock times, regularized/late/early flags). Computed live from the source
    /// data so the drill-down reflects the latest punches.
    /// </summary>
    Task<Result<EmployeeDailyBreakdownResult>> GetEmployeeBreakdownAsync(
        Guid employeeId, int year, int month, CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-3/FR-4: (re)computes and upserts the monthly summary for every (filtered) employee for the
    /// month and returns a COMPLETED status. For the current incomplete month it computes up to today.
    /// </summary>
    Task<Result<SummaryGenerationStatusDto>> GenerateAsync(
        int year, int month, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR-7 (US-ATT-009 ISSUE-091): computes ONE employee's present/absent/LOP/work rollup for a month up
    /// to a last-working-day <paramref name="cutoff"/> (inclusive), using the same day-by-day engine as the
    /// materialized summary. The materialized monthly summary EXCLUDES terminated employees, so the payroll
    /// feed uses this to include a terminated employee's pre-termination attendance (incl. LOP) instead of
    /// zeroing it. Returns null when the employee does not exist or the cutoff precedes the month start.
    /// </summary>
    Task<Result<EmployeeMonthlySummaryDto?>> ComputeForEmployeeUpToAsync(
        Guid employeeId, int year, int month, DateOnly cutoff, CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-4/FR-6: exports the monthly summary in CSV / XLSX / PDF. Small datasets render synchronously;
    /// &gt; 1,000 employees route to a Hangfire background job (FR-7 — notification deferred, US-NTF).
    /// </summary>
    Task<Result<MonthlySummaryExportResult>> ExportAsync(
        int year, int month, string format, MonthlySummaryFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders an already-materialized result to a file (used by both the sync export path and the
    /// background export job so the two are byte-identical). format = csv|xlsx|pdf.
    /// </summary>
    (byte[] Content, string FileName, string ContentType) RenderExport(
        int year, int month, string format, MonthlySummaryResult result);
}
