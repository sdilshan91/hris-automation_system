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
}
