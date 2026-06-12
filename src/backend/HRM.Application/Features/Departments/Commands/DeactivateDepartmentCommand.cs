using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Departments.Commands;

/// <summary>
/// Deactivates (soft-deletes) a department (US-CHR-004 AC-5, FR-6, FR-7).
/// Blocks if the department has active children (BR-6).
/// Blocks if the department has active employees assigned (AC-5) -
/// NOTE: Employee entity is deferred to US-CHR-001; for now the handler
/// always reports 0 active employees.
/// </summary>
public sealed record DeactivateDepartmentCommand(
    Guid DepartmentId
) : IRequest<Result>;
