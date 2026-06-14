using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>
/// Command for a manager to reject a pending regularization from a direct report (US-ATT-004 AC-2).
/// The reason is mandatory, minimum 10 characters (BR-1).
/// </summary>
public sealed record RejectRegularizationCommand(
    Guid RegularizationId,
    string Reason) : IRequest<Result<RegularizationDecisionDto>>;
