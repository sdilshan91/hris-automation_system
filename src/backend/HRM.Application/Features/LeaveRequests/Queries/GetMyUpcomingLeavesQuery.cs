using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

/// <summary>
/// Query for the current employee's upcoming leaves (US-LV-006 FR-4, AC-3): Approved and
/// Pending leave requests with start_date &gt;= today, ordered by start date.
/// </summary>
public sealed record GetMyUpcomingLeavesQuery
    : IRequest<Result<IReadOnlyList<UpcomingLeaveDto>>>;
