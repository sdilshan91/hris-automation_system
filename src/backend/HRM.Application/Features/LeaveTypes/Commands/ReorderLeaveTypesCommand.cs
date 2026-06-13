using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.LeaveTypes.Commands;

/// <summary>
/// Reorders leave types by display_order (US-LV-001 FR-3).
/// </summary>
public sealed record ReorderLeaveTypesCommand(
    IReadOnlyList<Guid> LeaveTypeIds
) : IRequest<Result>;
