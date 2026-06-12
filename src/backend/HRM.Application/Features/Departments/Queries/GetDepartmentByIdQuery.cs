using HRM.Application.Common.Models;
using HRM.Application.Features.Departments.DTOs;
using MediatR;

namespace HRM.Application.Features.Departments.Queries;

/// <summary>
/// Gets a single department by its ID within the current tenant.
/// </summary>
public sealed record GetDepartmentByIdQuery(
    Guid DepartmentId
) : IRequest<Result<DepartmentDto>>;
