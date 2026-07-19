using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.SalaryGrades.Commands;

/// <summary>
/// Deactivates (soft-deletes) a salary grade in the current tenant (Payroll domain, ISSUE-021).
/// </summary>
public sealed record DeactivateSalaryGradeCommand(Guid SalaryGradeId) : IRequest<Result>;
