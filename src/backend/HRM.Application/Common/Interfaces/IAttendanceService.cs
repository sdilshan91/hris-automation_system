using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service for employee attendance punches (US-ATT-001). All operations are tenant-scoped via
/// ITenantContext and EF global query filters; the acting employee is resolved from the current user.
/// </summary>
public interface IAttendanceService
{
    /// <summary>
    /// Clocks the current employee in, enforcing FR-1..FR-5, AC-1..AC-5, and BR-1/BR-2/BR-3/BR-6.
    /// Returns (failures carry a stable ErrorCode the client keys on, with an HTTP-status fallback):
    ///   400 when the tenant is unresolved, geolocation is required but missing (AC-3),
    ///       or a required photo is missing (BR-6);
    ///   403 when no employee record is linked to the user, the employee is not active (BR-5),
    ///       the source IP is not on the allowlist (AC-5/FR-4, code "ip_not_allowed"), or the
    ///       coordinates fall outside the geo-fence (FR-3, code "geo_fence_violation");
    ///   409 when the employee already has an open (un-clocked-out) record
    ///       (AC-2/BR-1, code "already_clocked_in").
    /// Idempotent within a short window via the supplied Idempotency-Key (NFR-4).
    /// </summary>
    Task<Result<AttendanceLogDto>> ClockInAsync(
        ClockInData data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clocks the current employee out and auto-calculates work hours
    /// (US-ATT-002 FR-1..FR-4/FR-6/FR-7, AC-1..AC-5, BR-1..BR-4/BR-6). Closes the employee's open
    /// AttendanceLog in a single atomic SaveChanges (NFR-3): sets clock_out (UTC, server-side, FR-1),
    /// total_work_minutes after the auto-break deduction (FR-2/FR-3, BR-2), overtime_minutes
    /// (FR-4, BR-3), and the outcome status (COMPLETE/SHORT_DAY/OVERTIME/ANOMALY, BR-4/BR-6).
    /// Returns (failures carry a stable ErrorCode with an HTTP-status fallback):
    ///   400 when the tenant is unresolved, or geolocation is required by policy but missing (AC-5);
    ///   403 when no employee record is linked to the user;
    ///   404 (code "no_active_clock_in") when the employee has no open record (AC-2/BR-1).
    /// FR-5 (Redis cache update) is deferred — this codebase has no Redis (consistent with US-ATT-001).
    /// </summary>
    Task<Result<ClockOutResultDto>> ClockOutAsync(
        ClockOutData data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current clock-in status for the acting employee (open AttendanceLog + the tenant's
    /// RequireGeolocation flag, BR-2, plus the most-recently-closed session summary, US-ATT-002).
    /// Shift fields are null until US-ATT-005. Tenant-scoped via the EF global query filter; the
    /// acting employee is resolved from the current user.
    /// Fails with 400 when the tenant context is unresolved, 403 when no employee is linked to the user.
    /// </summary>
    Task<Result<ClockStatusDto>> GetClockStatusAsync(
        CancellationToken cancellationToken = default);
}
