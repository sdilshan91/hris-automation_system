using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

/// <summary>
/// Query to list the current employee's own leave requests (US-LV-003 "My Leaves" / US-LV-006 FR-6),
/// with optional server-side history filters (ISSUE-038): status, leave type, and year (of the leave's
/// start date). A null filter means "no restriction on that dimension".
/// </summary>
public sealed record GetMyLeaveRequestsQuery(
    string? Status = null,
    Guid? LeaveTypeId = null,
    int? Year = null
) : IRequest<Result<IReadOnlyList<LeaveRequestDto>>>;
