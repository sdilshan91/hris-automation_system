using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using HRM.Application.Features.Employees.Queries;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service interface for bulk employee import operations (US-CHR-010).
/// All operations are tenant-scoped via ITenantContext.
/// </summary>
public interface IBulkEmployeeImportService
{
    /// <summary>
    /// Generates a downloadable import template (FR-2, AC-1).
    /// </summary>
    Task<Result<ExportFileResult>> GenerateTemplateAsync(
        ExportFormat format,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports employees from file content. Synchronous for <= 500 rows,
    /// queues a Hangfire job for > 500 rows (FR-7, AC-4).
    /// </summary>
    Task<Result<BulkImportResult>> ImportAsync(
        Stream fileStream,
        string fileName,
        long fileSize,
        bool importUpToLimit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a bulk import job asynchronously (called by Hangfire).
    /// Tenant context must be set before calling.
    /// </summary>
    Task ProcessImportJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of an import job (AC-4).
    /// </summary>
    Task<Result<BulkImportJobStatus>> GetJobStatusAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a CSV error report for a completed import job (FR-8).
    /// </summary>
    Task<Result<ExportFileResult>> GetErrorReportAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);
}
