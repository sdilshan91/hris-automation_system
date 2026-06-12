using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

/// <summary>
/// Extended employee directory query with search, multi-select filters, sorting,
/// configurable page size, and role-based visibility (US-CHR-003).
/// </summary>
public sealed record GetEmployeeDirectoryQuery(
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
    int Page = 1,
    int PageSize = 20,
    bool ShowArchived = false
) : IRequest<Result<EmployeeDirectoryResult>>;
