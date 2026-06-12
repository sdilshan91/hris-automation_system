using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Commands;

/// <summary>
/// MediatR command for bulk manager assignment (US-CHR-011 FR-4, AC-5).
/// </summary>
public sealed record BulkAssignManagerCommand(
    IReadOnlyList<Guid> EmployeeIds,
    Guid ManagerEmployeeId,
    string? Reason) : IRequest<Result<BulkAssignManagerResult>>;
