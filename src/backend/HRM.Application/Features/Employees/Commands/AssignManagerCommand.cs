using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Commands;

/// <summary>
/// MediatR command for assigning a reporting manager to an employee (US-CHR-011 AC-2).
/// </summary>
public sealed record AssignManagerCommand(
    Guid EmployeeId,
    Guid? ManagerEmployeeId,
    string? Reason) : IRequest<Result<AssignManagerResult>>;
