using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

/// <summary>
/// Downloads a bulk import template in CSV or Excel format (US-CHR-010 FR-2, AC-1).
/// </summary>
public sealed record GetBulkImportTemplateQuery(
    ExportFormat Format
) : IRequest<Result<ExportFileResult>>;
