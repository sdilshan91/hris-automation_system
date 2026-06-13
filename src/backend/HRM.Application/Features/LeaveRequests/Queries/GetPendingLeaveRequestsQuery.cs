using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

/// <summary>
/// Query for a manager's pending-leave-approval queue (US-LV-004 FR-1..FR-5).
/// Returns the pending requests of the current manager's direct reports (BR-1),
/// tenant-scoped, with inline balance, overdue flag, and team-conflict count,
/// filtered/sorted/paged per the supplied parameters.
/// </summary>
public sealed record GetPendingLeaveRequestsQuery(PendingLeaveQueueQueryParams Params)
    : IRequest<Result<PendingLeaveQueueResult>>;
