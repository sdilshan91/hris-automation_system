using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Commands;

/// <summary>
/// Updates an employee's profile with field-level permission enforcement,
/// optimistic concurrency via xmin, audit logging, and employment history (US-CHR-002).
/// </summary>
public sealed record UpdateEmployeeProfileCommand(
    Guid EmployeeId,
    UpdateEmployeeProfileRequest Request
) : IRequest<Result<EmployeeProfileDto>>;
