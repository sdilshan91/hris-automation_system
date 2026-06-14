using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>
/// US-ATT-007 AC-2: day-by-day attendance breakdown for one employee for a month.
/// </summary>
public sealed record GetEmployeeMonthlyBreakdownQuery(Guid EmployeeId, int Year, int Month)
    : IRequest<Result<EmployeeDailyBreakdownResult>>;
