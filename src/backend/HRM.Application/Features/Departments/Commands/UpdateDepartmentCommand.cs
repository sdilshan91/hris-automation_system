using HRM.Application.Common.Models;
using HRM.Application.Features.Departments.DTOs;
using MediatR;

namespace HRM.Application.Features.Departments.Commands;

/// <summary>
/// Updates an existing department (US-CHR-004 AC-4).
/// Validates hierarchy changes for circular references (FR-5).
/// </summary>
public sealed record UpdateDepartmentCommand(
    Guid DepartmentId,
    string Name,
    string Code,
    string? Description,
    Guid? ParentDepartmentId,
    Guid? ManagerId
) : IRequest<Result<DepartmentDto>>;
