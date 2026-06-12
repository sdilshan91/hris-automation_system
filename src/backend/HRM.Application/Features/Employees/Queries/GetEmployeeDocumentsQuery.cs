using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

/// <summary>
/// Lists documents for an employee (US-CHR-008 FR-9).
/// </summary>
public sealed record GetEmployeeDocumentsQuery(
    Guid EmployeeId,
    string? Category = null
) : IRequest<Result<EmployeeDocumentListResult>>;
