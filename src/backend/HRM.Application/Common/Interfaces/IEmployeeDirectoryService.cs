using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using HRM.Application.Features.Employees.Queries;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service interface for employee directory search, filter, and export (US-CHR-003).
/// All operations are tenant-scoped via ITenantContext.
/// </summary>
public interface IEmployeeDirectoryService
{
    /// <summary>
    /// Returns a paginated, filtered, sorted directory listing with role-based field stripping.
    /// </summary>
    Task<Result<EmployeeDirectoryResult>> GetDirectoryAsync(
        string? search,
        IReadOnlyList<Guid>? departmentIds,
        IReadOnlyList<string>? jobTitles,
        IReadOnlyList<string>? statuses,
        IReadOnlyList<string>? employmentTypes,
        IReadOnlyList<string>? locations,
        DateTime? dateOfJoiningFrom,
        DateTime? dateOfJoiningTo,
        string? sortBy,
        bool sortDescending,
        int page,
        int pageSize,
        bool showArchived,
        DirectoryFieldVisibility visibility,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports the filtered directory as CSV or Excel, respecting role-based field visibility (FR-8, BR-4).
    /// Synchronous implementation; async/Hangfire for very large datasets deferred to NFR-5.
    /// </summary>
    Task<Result<ExportFileResult>> ExportDirectoryAsync(
        ExportFormat format,
        string? search,
        IReadOnlyList<Guid>? departmentIds,
        IReadOnlyList<string>? jobTitles,
        IReadOnlyList<string>? statuses,
        IReadOnlyList<string>? employmentTypes,
        IReadOnlyList<string>? locations,
        DateTime? dateOfJoiningFrom,
        DateTime? dateOfJoiningTo,
        string? sortBy,
        bool sortDescending,
        bool showArchived,
        DirectoryFieldVisibility visibility,
        CancellationToken cancellationToken = default);
}
