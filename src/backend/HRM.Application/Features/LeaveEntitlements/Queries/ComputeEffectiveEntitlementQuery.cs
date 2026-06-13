using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Queries;

/// <summary>
/// Computes the effective entitlement for an employee, leave type, and year (US-LV-002 FR-2).
/// </summary>
public sealed record ComputeEffectiveEntitlementQuery(
    Guid EmployeeId,
    Guid LeaveTypeId,
    int LeaveYear
) : IRequest<Result<EffectiveEntitlementDto>>;
