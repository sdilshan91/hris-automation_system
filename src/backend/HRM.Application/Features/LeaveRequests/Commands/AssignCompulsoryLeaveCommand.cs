using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Commands;

/// <summary>
/// HR bulk-assigns a compulsory leave (company shutdown) to all/selected employees for the given
/// dates (US-LV-011 FR-6, BR-4). Delegates to <c>ILopService.AssignCompulsoryLeaveAsync</c>.
/// </summary>
public sealed record AssignCompulsoryLeaveCommand(CompulsoryLeaveRequest Request)
    : IRequest<Result<CompulsoryLeaveResultDto>>;
