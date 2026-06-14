using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>
/// Command: the acting employee submits an overtime pre-approval request (US-ATT-006 AC-2/FR-4).
/// </summary>
public sealed record SubmitOvertimePreApprovalCommand(
    OvertimePreApprovalRequest Request) : IRequest<Result<OvertimeDto>>;
