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
        bool includeTerminated = false,
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

    /// <summary>
    /// ISSUE-293: reveals the FULL decrypted national identity number for a tenant-scoped employee. The caller
    /// must hold Employee.View.All (enforced at the controller). Writes an <c>Employee.NationalId.ViewSensitive</c>
    /// audit row (naming the field only, never the value) on every authorized access. 404 when the employee is
    /// not found in the current tenant; a null NationalId is returned as null (still audited).
    /// </summary>
    Task<Result<NationalIdRevealDto>> RevealNationalIdAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default);
}
