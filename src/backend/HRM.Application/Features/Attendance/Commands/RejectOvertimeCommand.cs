using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>
/// Command: a manager rejects an overtime record from a direct report (US-ATT-006). Reason mandatory,
/// minimum 10 characters.
/// </summary>
public sealed record RejectOvertimeCommand(
    Guid OvertimeId,
    string Reason) : IRequest<Result<OvertimeDecisionDto>>;
