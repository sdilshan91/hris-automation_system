using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Commands;

/// <summary>
/// Command for a manager to approve a pending leave request from a direct report (US-LV-005 FR-1).
/// Comment is optional (BR-2).
/// </summary>
public sealed record ApproveLeaveRequestCommand(
    Guid LeaveRequestId,
    string? Comment) : IRequest<Result<LeaveApprovalResultDto>>;
