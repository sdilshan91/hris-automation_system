using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service interface for employee CRUD operations (US-CHR-001).
/// All operations are tenant-scoped via ITenantContext.
/// </summary>
public interface IEmployeeService
{
    Task<Result<EmployeeDto>> CreateAsync(
        CreateEmployeeRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<EmployeeDto>> GetByIdAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default);

    Task<Result<EmployeeListResult>> GetAllAsync(
        int page = 1,
        int pageSize = 20,
        bool? activeOnly = null,
        string? search = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads and validates a profile photo for an employee.
    /// </summary>
    Task<Result<string>> UploadProfilePhotoAsync(
        Guid employeeId,
        Stream fileStream,
        string fileName,
        string contentType,
        long fileSize,
        CancellationToken cancellationToken = default);
}
