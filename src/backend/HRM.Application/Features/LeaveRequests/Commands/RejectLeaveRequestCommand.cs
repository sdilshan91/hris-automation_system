using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Commands;

/// <summary>
/// Command for a manager to reject a pending leave request from a direct report (US-LV-005 FR-2).
/// Reason is mandatory (BR-2, AC-2) — enforced by the validator.
/// </summary>
public sealed record RejectLeaveRequestCommand(
    Guid LeaveRequestId,
    string Reason) : IRequest<Result<LeaveApprovalResultDto>>;
