using HRM.Application.Common.Models;
using HRM.Application.Features.Departments.DTOs;
using MediatR;

namespace HRM.Application.Features.Departments.Commands;

/// <summary>
/// Creates a new department in the current tenant (US-CHR-004 AC-2).
/// </summary>
public sealed record CreateDepartmentCommand(
    string Name,
    string Code,
    string? Description,
    Guid? ParentDepartmentId,
    Guid? ManagerId
) : IRequest<Result<DepartmentDto>>;
