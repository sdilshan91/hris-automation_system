using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service interface for employee CRUD operations (US-CHR-001, US-CHR-002).
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

    /// <summary>
    /// Gets a comprehensive employee profile with all sections (US-CHR-002 AC-1).
    /// Includes emergency contacts, employment history, and the xmin concurrency token.
    /// </summary>
    Task<Result<EmployeeProfileDto>> GetProfileAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an employee profile with field-level permission checks,
    /// optimistic concurrency via xmin, audit logging, and employment history (US-CHR-002).
    /// </summary>
    Task<Result<EmployeeProfileDto>> UpdateProfileAsync(
        Guid employeeId,
        UpdateEmployeeProfileRequest request,
        CancellationToken cancellationToken = default);
}
