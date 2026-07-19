using HRM.Application.Common.Models;
using HRM.Application.Features.SalaryGrades.DTOs;
using MediatR;

namespace HRM.Application.Features.SalaryGrades.Commands;

/// <summary>
/// Creates a new salary grade in the current tenant (Payroll domain, ISSUE-021).
/// </summary>
public sealed record CreateSalaryGradeCommand(
    string Code,
    string Name,
    decimal MinAmount,
    decimal? MidAmount,
    decimal MaxAmount,
    string Currency,
    string? Description
) : IRequest<Result<SalaryGradeDto>>;
