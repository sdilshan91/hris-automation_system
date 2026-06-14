using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Commands;

/// <summary>
/// Command for an employee to cancel their OWN leave request (US-LV-010 FR-1).
/// Works for both pending and approved requests. The reason is mandatory for approved leaves
/// (BR-5) and optional for pending ones — enforced in the service (which knows the status);
/// the validator only bounds the shape.
/// </summary>
public sealed record CancelLeaveRequestCommand(
    Guid LeaveRequestId,
    string? Reason) : IRequest<Result<LeaveCancellationResultDto>>;
