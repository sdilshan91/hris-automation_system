using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Late-arrival / early-departure policy + reporting service (US-ATT-008). The actual late/early
/// DETECTION is inline on the clock-in/clock-out path (AttendanceService, NFR-1) and on regularization
/// approval (RegularizationApprovalService, BR-7); this service owns the tenant POLICY (FR-4), the
/// team/HR REPORT (AC-5/FR-6) and the employee SELF-SCORE (§8) — all reading the persisted
/// is_late/late_minutes/is_early_departure/early_departure_minutes fields on attendance_log.
/// Tenant-scoped via ITenantContext + EF global query filters (no RLS — same as the rest of the module).
/// </summary>
public interface ILateEarlyService
{
    /// <summary>
    /// FR-4: returns the tenant late policy, lazily creating a default (inactive) row when none exists.
    /// Fails 400 when the tenant is unresolved. Requires the HR permission (gated at the controller).
    /// </summary>
    Task<Result<LatePolicyDto>> GetPolicyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-4: upserts the tenant late policy (HR). Validates period/threshold/deduction. Fails 400 when
    /// the tenant is unresolved or the payload is invalid.
    /// </summary>
    Task<Result<LatePolicyDto>> UpsertPolicyAsync(
        LatePolicyDto policy, CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-5/FR-6: late/early-departure report for a date range. scope="team" → the acting manager's
    /// direct reports (Attendance.Approve.Team); scope="all" → every employee (HR, Attendance.View.All).
    /// A scope="all" caller without Attendance.View.All is rejected 403. Optional department/employee
    /// filters. Reads persisted is_late/late_minutes/is_early_departure/early_departure_minutes.
    /// </summary>
    Task<Result<LateEarlyReportResult>> GetReportAsync(
        DateOnly from, DateOnly to, Guid? departmentId, Guid? employeeId, string scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// §8: the acting employee's own monthly lateness score ("X of N allowed lates"). Fails 400 when the
    /// tenant is unresolved or the month string is malformed, 403 when no employee is linked to the user.
    /// </summary>
    Task<Result<LatenessScoreDto>> GetMyScoreAsync(
        int year, int month, CancellationToken cancellationToken = default);
}
