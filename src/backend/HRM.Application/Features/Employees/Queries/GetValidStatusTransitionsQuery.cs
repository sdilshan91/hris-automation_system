using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

/// <summary>
/// Query to get valid status transitions for an employee (US-CHR-009 FR-2).
/// </summary>
public sealed record GetValidStatusTransitionsQuery(Guid EmployeeId) : IRequest<Result<ValidTransitionsResult>>;
