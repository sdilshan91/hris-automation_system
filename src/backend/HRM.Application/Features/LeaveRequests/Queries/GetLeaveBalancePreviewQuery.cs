using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

/// <summary>
/// Query for the inline balance preview shown on the leave-application form (US-LV-003 FR-2, AC-2).
/// Date range is optional; when supplied, RequestedDays/ProjectedBalance are computed.
/// </summary>
public sealed record GetLeaveBalancePreviewQuery(
    Guid LeaveTypeId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    bool IsHalfDay) : IRequest<Result<LeaveBalancePreviewDto>>;
