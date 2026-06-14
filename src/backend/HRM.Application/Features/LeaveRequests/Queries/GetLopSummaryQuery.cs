using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

/// <summary>
/// Read-only LOP summary for an employee over a period, consumed by payroll (US-LV-011 FR-5, AC-4).
/// Delegates to <c>ILopService.GetLopSummaryAsync</c>.
/// </summary>
public sealed record GetLopSummaryQuery(Guid EmployeeId, DateOnly From, DateOnly To)
    : IRequest<Result<LopSummaryDto>>;
