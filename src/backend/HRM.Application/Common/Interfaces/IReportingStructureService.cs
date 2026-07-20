using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service interface for employee reporting structure management (US-CHR-011).
/// Handles manager assignment, cycle detection, direct reports, and bulk operations.
/// All operations are tenant-scoped via ITenantContext.
/// </summary>
public interface IReportingStructureService
{
    /// <summary>
    /// Assigns or unassigns a reporting manager for an employee (AC-2, FR-1, FR-8).
    /// Detects cycles (FR-3, AC-3), validates active manager (BR-3), rejects self-assignment (BR-7),
    /// records employment history and audit (FR-6, NFR-5).
    /// </summary>
    Task<Result<AssignManagerResult>> AssignManagerAsync(
        Guid employeeId,
        AssignManagerRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk-assigns a manager to multiple employees (FR-4, AC-5).
    /// Validates each individually and returns per-employee success/failure.
    /// </summary>
    Task<Result<BulkAssignManagerResult>> BulkAssignManagerAsync(
        BulkAssignManagerRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns direct reports for a manager (FR-5, AC-4).
    /// </summary>
    Task<Result<DirectReportsResult>> GetDirectReportsAsync(
        Guid managerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full ascending reporting chain for an employee (DF-8 / ISSUE-218):
    /// element 0 is the employee themselves, then each manager up to the top/root.
    /// The walk is cycle-guarded and depth-capped; a soft-deleted/absent manager
    /// truncates the chain at that rung.
    /// </summary>
    Task<Result<ReportingChainDto>> GetReportingChainAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default);
}
