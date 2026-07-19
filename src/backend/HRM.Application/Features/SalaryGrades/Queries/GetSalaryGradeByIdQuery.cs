using HRM.Application.Common.Models;
using HRM.Application.Features.SalaryGrades.DTOs;
using MediatR;

namespace HRM.Application.Features.SalaryGrades.Queries;

/// <summary>
/// Gets a single salary grade by id for the current tenant (Payroll domain, ISSUE-021).
/// </summary>
public sealed record GetSalaryGradeByIdQuery(Guid SalaryGradeId)
    : IRequest<Result<SalaryGradeDto>>;
