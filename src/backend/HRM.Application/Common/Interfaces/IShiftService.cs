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

    Task<Result<AssignmentResultDto>> AssignAsync(
        Guid shiftId, IReadOnlyList<Guid> employeeIds, DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    Task<Result<ResolvedShiftDto>> ResolveForEmployeeAsync(
        Guid employeeId, DateOnly date, CancellationToken cancellationToken = default);
}
