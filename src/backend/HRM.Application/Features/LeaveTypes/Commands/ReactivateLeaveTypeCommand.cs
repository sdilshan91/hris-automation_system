using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.LeaveTypes.Commands;

/// <summary>
/// Reactivates a previously deactivated leave type (US-LV-001).
/// </summary>
public sealed record ReactivateLeaveTypeCommand(Guid LeaveTypeId) : IRequest<Result>;
