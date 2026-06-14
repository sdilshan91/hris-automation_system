using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveTypes.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service interface for leave type CRUD operations (US-LV-001).
/// All operations are tenant-scoped via ITenantContext.
/// </summary>
public interface ILeaveTypeService
{
    Task<Result<LeaveTypeDto>> CreateAsync(
        CreateLeaveTypeRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<LeaveTypeDto>> UpdateAsync(
        Guid leaveTypeId,
        UpdateLeaveTypeRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> DeactivateAsync(
        Guid leaveTypeId,
        CancellationToken cancellationToken = default);

    Task<Result> ReactivateAsync(
        Guid leaveTypeId,
        CancellationToken cancellationToken = default);

    Task<Result<LeaveTypeDto>> GetByIdAsync(
        Guid leaveTypeId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<LeaveTypeDto>>> GetAllAsync(
        bool? activeOnly = null,
        CancellationToken cancellationToken = default);

    Task<Result> ReorderAsync(
        IReadOnlyList<Guid> leaveTypeIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Seeds default leave types for a newly provisioned tenant (FR-4).
    /// Idempotent: skips if any leave types already exist for the tenant.
    /// </summary>
    Task<Result> SeedDefaultsForTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the system "Loss of Pay" (LOP) leave type exists for the tenant (US-LV-011 FR-1, BR-1):
    /// zero entitlement, no balance, flagged with SystemCategory = LossOfPay so it cannot be deleted
    /// but can be renamed. Idempotent — returns the existing LOP type if one already exists. Returns the
    /// LOP type id. Used by the LOP flows (assign-lop, compulsory, absenteeism job) to resolve the type.
    /// </summary>
    Task<Result<Guid>> EnsureLopTypeForTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
