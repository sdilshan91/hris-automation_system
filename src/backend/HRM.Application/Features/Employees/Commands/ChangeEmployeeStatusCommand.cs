using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Employees.Commands;

/// <summary>
/// MediatR command for changing an employee's status (US-CHR-009 FR-3).
/// </summary>
public sealed record ChangeEmployeeStatusCommand(
    Guid EmployeeId,
    EmployeeStatus NewStatus,
    string Reason,
    DateTime EffectiveDate,
    string? IdempotencyKey) : IRequest<Result<ChangeEmployeeStatusResult>>;
