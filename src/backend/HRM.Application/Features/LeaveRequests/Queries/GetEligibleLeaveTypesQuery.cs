using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveTypes.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

/// <summary>
/// Query for the leave types the current employee is eligible to apply for (US-LV-003 FR-1 / ISSUE-035),
/// with the BR-4 gender and BR-5 probation gates applied — the apply-form dropdown source.
/// </summary>
public sealed record GetEligibleLeaveTypesQuery : IRequest<Result<IReadOnlyList<LeaveTypeDto>>>;
