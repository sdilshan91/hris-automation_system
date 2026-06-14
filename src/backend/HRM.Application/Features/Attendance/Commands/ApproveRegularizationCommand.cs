using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>
/// Command for a manager to approve a pending regularization from a direct report (US-ATT-004 AC-1).
/// Final single-level approve: updates the attendance_log. Comment is optional (BR-2).
/// </summary>
public sealed record ApproveRegularizationCommand(
    Guid RegularizationId,
    string? Comment) : IRequest<Result<RegularizationDecisionDto>>;
