using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Commands;

/// <summary>
/// Uploads and processes a bulk employee import file (US-CHR-010 AC-2/AC-3/AC-4).
/// For <= 500 rows: processes synchronously and returns results.
/// For > 500 rows: queues a Hangfire job and returns the job ID.
/// </summary>
public sealed record BulkImportEmployeesCommand(
    Stream FileStream,
    string FileName,
    long FileSize,
    bool ImportUpToLimit = false
) : IRequest<Result<BulkImportResult>>;
