using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Commands;

/// <summary>
/// Command to submit a new leave request for the current employee (US-LV-003 FR-5).
/// </summary>
public sealed record CreateLeaveRequestCommand(
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsHalfDay,
    string? HalfDaySession,
    string? Reason,
    IReadOnlyList<string>? Attachments) : IRequest<Result<LeaveRequestDto>>;
