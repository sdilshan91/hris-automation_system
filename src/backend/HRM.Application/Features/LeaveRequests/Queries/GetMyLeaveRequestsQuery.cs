using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

/// <summary>
/// Query to list the current employee's own leave requests (US-LV-003, "My Leaves").
/// </summary>
public sealed record GetMyLeaveRequestsQuery : IRequest<Result<IReadOnlyList<LeaveRequestDto>>>;
