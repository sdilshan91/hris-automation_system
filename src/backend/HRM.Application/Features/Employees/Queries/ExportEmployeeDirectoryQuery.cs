using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

/// <summary>
/// Export format for the directory export (US-CHR-003 FR-8, AC-5).
/// </summary>
public enum ExportFormat
{
    Csv,
    Excel,
}

/// <summary>
/// Exports the filtered employee directory as CSV or Excel (US-CHR-003 FR-8).
/// Synchronous path; async/Hangfire for very large datasets is deferred (NFR-5).
/// </summary>
public sealed record ExportEmployeeDirectoryQuery(
    ExportFormat Format,
    string? Search = null,
    IReadOnlyList<Guid>? DepartmentIds = null,
    IReadOnlyList<string>? JobTitles = null,
    IReadOnlyList<string>? Statuses = null,
    IReadOnlyList<string>? EmploymentTypes = null,
    IReadOnlyList<string>? Locations = null,
    DateTime? DateOfJoiningFrom = null,
    DateTime? DateOfJoiningTo = null,
    string? SortBy = null,
    bool SortDescending = false,
    bool ShowArchived = false
) : IRequest<Result<ExportFileResult>>;

/// <summary>
/// Result of a directory export operation.
/// </summary>
public sealed record ExportFileResult(
    byte[] FileBytes,
    string ContentType,
    string FileName);
