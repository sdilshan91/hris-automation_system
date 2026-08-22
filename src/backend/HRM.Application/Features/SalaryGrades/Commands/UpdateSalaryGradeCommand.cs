using HRM.Application.Common.Models;
using HRM.Application.Features.SalaryGrades.DTOs;
using MediatR;

namespace HRM.Application.Features.SalaryGrades.Commands;

/// <summary>
/// Updates an existing salary grade in the current tenant (Payroll domain, ISSUE-021).
/// </summary>
public sealed record UpdateSalaryGradeCommand(
    Guid SalaryGradeId,
    string Code,
    string Name,
    decimal MinAmount,
    decimal? MidAmount,
    decimal MaxAmount,
    string Currency,
    string? Description,
    bool? IsActive
) : IRequest<Result<SalaryGradeDto>>;
