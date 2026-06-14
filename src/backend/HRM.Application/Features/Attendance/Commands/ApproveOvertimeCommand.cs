using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>
/// Command: a manager approves an overtime record from a direct report (US-ATT-006 AC-4/FR-6/FR-7).
/// approvedMinutes defaults to the recorded overtime; the manager may adjust it down (FR-6).
/// </summary>
public sealed record ApproveOvertimeCommand(
    Guid OvertimeId,
    int? ApprovedMinutes,
    string? Comment) : IRequest<Result<OvertimeDecisionDto>>;
