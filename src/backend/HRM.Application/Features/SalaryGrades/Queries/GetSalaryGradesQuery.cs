using HRM.Application.Common.Models;
using HRM.Application.Features.SalaryGrades.DTOs;
using MediatR;

namespace HRM.Application.Features.SalaryGrades.Queries;

/// <summary>
/// Lists salary grades for the current tenant (Payroll domain, ISSUE-021).
/// </summary>
public sealed record GetSalaryGradesQuery(bool IncludeInactive = false)
    : IRequest<Result<IReadOnlyList<SalaryGradeDto>>>;
