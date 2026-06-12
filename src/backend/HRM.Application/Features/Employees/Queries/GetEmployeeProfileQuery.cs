using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

/// <summary>
/// Gets a comprehensive employee profile by ID (US-CHR-002 AC-1).
/// Includes all sections: personal, contact, emergency contacts,
/// employment, employment history, custom fields.
/// </summary>
public sealed record GetEmployeeProfileQuery(
    Guid EmployeeId
) : IRequest<Result<EmployeeProfileDto>>;
