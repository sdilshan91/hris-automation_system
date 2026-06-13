using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Commands;

/// <summary>
/// Creates or updates a per-employee leave entitlement override (US-LV-002 AC-3).
/// </summary>
public sealed record UpsertLeaveEntitlementOverrideCommand(
    Guid EmployeeId,
    Guid LeaveTypeId,
    int LeaveYear,
    decimal EntitlementDays,
    string Reason
) : IRequest<Result<LeaveEntitlementOverrideDto>>;
