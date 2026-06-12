using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

/// <summary>
/// Gets a single employee by ID within the current tenant.
/// </summary>
public sealed record GetEmployeeByIdQuery(
    Guid EmployeeId
) : IRequest<Result<EmployeeDto>>;
