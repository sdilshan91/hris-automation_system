using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Employees.Commands;

/// <summary>
/// Soft-deletes an employee document (US-CHR-008 FR-7, BR-5).
/// HR Officer only.
/// </summary>
public sealed record DeleteEmployeeDocumentCommand(
    Guid EmployeeId,
    Guid DocumentId
) : IRequest<Result>;
