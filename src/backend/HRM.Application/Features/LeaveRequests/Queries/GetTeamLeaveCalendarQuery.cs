using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

/// <summary>
/// Query for the team leave calendar (US-LV-009 FR-1..FR-4, FR-6/FR-7).
///
/// Row-level scope is resolved from the caller (AC-1/AC-2, BR-1..BR-3):
///   - Leave.View.All (HR): all employees' Approved + Pending leaves, full detail.
///   - Manager (has direct reports): direct reports' Approved + Pending, full detail.
///   - Employee: own-department colleagues' Approved leaves only, with leave-type/status
///     detail suppressed (BR-1).
/// Cancelled and Rejected leaves are excluded (BR-4). Public holidays in the range are
/// returned for background highlights (FR-7).
/// </summary>
public sealed record GetTeamLeaveCalendarQuery(TeamLeaveCalendarQueryParams Params)
    : IRequest<Result<TeamLeaveCalendarDto>>;
