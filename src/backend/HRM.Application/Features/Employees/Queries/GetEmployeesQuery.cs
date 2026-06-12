using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

/// <summary>
/// Lists employees for the current tenant with pagination and optional filters.
/// </summary>
public sealed record GetEmployeesQuery(
    int Page = 1,
    int PageSize = 20,
    bool? ActiveOnly = null,
    string? Search = null
) : IRequest<Result<EmployeeListResult>>;
