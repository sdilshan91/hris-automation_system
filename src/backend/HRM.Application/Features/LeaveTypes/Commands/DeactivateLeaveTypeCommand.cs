using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.LeaveTypes.Commands;

/// <summary>
/// Deactivates (soft-deactivates) a leave type (US-LV-001 AC-4, FR-5).
/// Hides from application forms but retains for historical reporting.
/// </summary>
public sealed record DeactivateLeaveTypeCommand(
    Guid LeaveTypeId
) : IRequest<Result>;
