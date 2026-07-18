using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Shift management and assignment service (US-ATT-005). All operations are tenant-scoped via
/// ITenantContext and the EF global query filters.
/// </summary>
public interface IShiftService
{
    Task<Result<IReadOnlyList<ShiftDto>>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<Result<ShiftDto>> CreateAsync(
        ShiftRequest request, CancellationToken cancellationToken = default);

    Task<Result<ShiftDto>> UpdateAsync(
        Guid shiftId, ShiftRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(
        Guid shiftId, CancellationToken cancellationToken = default);

    Task<Result<ShiftDto>> CloneAsync(
        Guid shiftId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ISSUE-077 (US-ATT-005 FR-5/BR-1): sets <paramref name="shiftId"/> as the tenant default working
    /// calendar, transferring the <c>is_default</c> flag off the current default so EXACTLY ONE shift is
    /// the default at any time. Idempotent (setting the current default again is a no-op); 404 when the
    /// shift does not exist in the tenant.
    /// </summary>
    Task<Result<ShiftDto>> SetDefaultAsync(
        Guid shiftId, CancellationToken cancellationToken = default);

    Task<Result<AssignmentResultDto>> AssignAsync(
        Guid shiftId, IReadOnlyList<Guid> employeeIds, DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    Task<Result<ResolvedShiftDto>> ResolveForEmployeeAsync(
        Guid employeeId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// ISSUE-076: resolves a shift with SELF-SCOPE authorization. A caller holding
    /// <c>Attendance.Shift.Manage</c> (HR) may resolve any employee's shift; any other caller may resolve
    /// ONLY their own (the employee linked to the current user). A non-manager requesting another
    /// employee's shift is denied 403; a caller with no linked employee record is denied 403.
    /// </summary>
    Task<Result<ResolvedShiftDto>> ResolveForEmployeeWithSelfScopeAsync(
        Guid employeeId, DateOnly date, CancellationToken cancellationToken = default);
}
