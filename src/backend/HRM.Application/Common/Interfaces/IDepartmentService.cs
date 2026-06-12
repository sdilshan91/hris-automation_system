using HRM.Application.Common.Models;
using HRM.Application.Features.Departments.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service interface for department CRUD operations (US-CHR-004).
/// All operations are tenant-scoped via ITenantContext.
/// </summary>
public interface IDepartmentService
{
    Task<Result<DepartmentDto>> CreateAsync(
        string name, string code, string? description,
        Guid? parentDepartmentId, Guid? managerId,
        CancellationToken cancellationToken = default);

    Task<Result<DepartmentDto>> UpdateAsync(
        Guid departmentId, string name, string code, string? description,
        Guid? parentDepartmentId, Guid? managerId,
        CancellationToken cancellationToken = default);

    Task<Result> DeactivateAsync(
        Guid departmentId,
        CancellationToken cancellationToken = default);

    Task<Result<DepartmentDto>> GetByIdAsync(
        Guid departmentId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<DepartmentDto>>> GetAllAsync(
        bool? activeOnly = null,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<DepartmentTreeNodeDto>>> GetTreeAsync(
        CancellationToken cancellationToken = default);
}
