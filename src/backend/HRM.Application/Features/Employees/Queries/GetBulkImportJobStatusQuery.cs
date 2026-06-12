using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

/// <summary>
/// Gets the status of an async bulk import job (US-CHR-010 AC-4).
/// </summary>
public sealed record GetBulkImportJobStatusQuery(
    Guid JobId
) : IRequest<Result<BulkImportJobStatus>>;

/// <summary>
/// Downloads the error report CSV for a completed import job (US-CHR-010 FR-8).
/// </summary>
public sealed record GetBulkImportErrorReportQuery(
    Guid JobId
) : IRequest<Result<ExportFileResult>>;
