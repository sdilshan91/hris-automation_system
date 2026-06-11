using HRM.Application.Common.Models;
using HRM.Application.Features.Departments.DTOs;
using MediatR;

namespace HRM.Application.Features.Departments.Queries;

/// <summary>
/// Lists all departments for the current tenant as a flat list.
/// Optionally filters by IsActive.
/// </summary>
public sealed record GetDepartmentsQuery(
    bool? ActiveOnly = null
) : IRequest<Result<IReadOnlyList<DepartmentDto>>>;
