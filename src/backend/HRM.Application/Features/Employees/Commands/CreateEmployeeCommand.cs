using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Employees.Commands;

/// <summary>
/// Creates a new employee in the current tenant (US-CHR-001 AC-2).
/// </summary>
public sealed record CreateEmployeeCommand(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    DateTime? DateOfBirth,
    Gender? Gender,
    DateTime DateOfJoining,
    Guid DepartmentId,
    Guid JobTitleId,
    EmploymentType EmploymentType,
    EmployeeStatus? Status,
    string? CustomFields,
    Guid? UserId
) : IRequest<Result<EmployeeDto>>;
