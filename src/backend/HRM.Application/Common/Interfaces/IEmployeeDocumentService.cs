using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service interface for employee document management (US-CHR-008).
/// All operations are tenant-scoped via ITenantContext.
/// </summary>
public interface IEmployeeDocumentService
{
    /// <summary>
    /// Uploads a document for an employee. Validates MIME, size, scans for viruses,
    /// strips EXIF from images, and stores in tenant-isolated object storage (FR-1 to FR-5).
    /// </summary>
    Task<Result<EmployeeDocumentDto>> UploadAsync(
        Guid employeeId,
        Stream fileStream,
        string fileName,
        string contentType,
        long fileSize,
        UploadEmployeeDocumentRequest metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists documents for an employee, tenant-scoped (FR-9).
    /// </summary>
    Task<Result<EmployeeDocumentListResult>> ListAsync(
        Guid employeeId,
        string? category = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a short-lived signed download URL for a document (FR-6, AC-4).
    /// Enforces authorization: HR Officer any, Employee own-record only, Manager blocked.
    /// </summary>
    Task<Result<DocumentDownloadResult>> GetDownloadUrlAsync(
        Guid employeeId,
        Guid documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a document. HR Officer only (FR-7, BR-5).
    /// </summary>
    Task<Result> DeleteAsync(
        Guid employeeId,
        Guid documentId,
        CancellationToken cancellationToken = default);
}
