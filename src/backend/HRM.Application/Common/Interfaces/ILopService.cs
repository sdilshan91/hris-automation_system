using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Compulsory-leave / Loss-of-Pay (LOP) handling service (US-LV-011).
/// All operations are tenant-scoped via ITenantContext and EF global query filters. LOP entries have
/// no balance (BR-1) — they are created as leave_request rows with is_lop = true against the system LOP
/// leave type, purely so payroll can compute the unpaid-day deduction (FR-5).
/// </summary>
public interface ILopService
{
    /// <summary>
    /// HR manually assigns LOP days to an employee for the given dates (FR-3, AC-3). Creates one
    /// leave_request per date with status HrAssigned, is_lop = true, lop_source = HrAssigned, against the
    /// system LOP leave type, plus an approval-history row and a notification (BR-6). Idempotent per date
    /// (skips dates already marked LOP for the employee).
    /// </summary>
    Task<Result<AssignLopResultDto>> AssignLopAsync(
        AssignLopRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the LOP entries and total day-count for an employee over a period, for payroll (FR-5).
    /// Read-only.
    /// </summary>
    Task<Result<LopSummaryDto>> GetLopSummaryAsync(
        Guid employeeId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk-assigns a compulsory leave (company shutdown) to all/selected employees for the given dates
    /// (FR-6). BR-4: deducts from the employee's balance for that leave type first; if insufficient,
    /// creates an LOP entry instead. Persists a CompulsoryLeave record per date and per-employee
    /// leave_request rows. Pages employees to stay reasonable for large tenants.
    /// </summary>
    Task<Result<CompulsoryLeaveResultDto>> AssignCompulsoryLeaveAsync(
        CompulsoryLeaveRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates auto-LOP entries for an employee's absent working days in a period (FR-2, AC-2), using
    /// the injected <see cref="IAttendanceProvider"/>. One leave_request per absent date with status
    /// SystemGenerated, is_lop = true, lop_source = SystemGenerated. Idempotent (skips dates already
    /// marked LOP). Returns the number of entries created. Used by ProcessAbsenteeismJob.
    /// </summary>
    Task<Result<int>> GenerateAbsenteeismLopAsync(
        Guid employeeId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// HR overrides a system-generated LOP entry (BR-3): converts it to a different (balance-backed)
    /// leave type — applying a "Used" deduction — or removes it (soft-cancel) when no target type is
    /// given. Writes audit. Only LOP entries can be overridden.
    /// </summary>
    Task<Result<OverrideLopResultDto>> OverrideLopAsync(
        Guid leaveRequestId,
        OverrideLopRequest request,
        CancellationToken cancellationToken = default);
}
