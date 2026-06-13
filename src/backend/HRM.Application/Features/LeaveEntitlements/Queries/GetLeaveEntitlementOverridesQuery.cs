using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Queries;

/// <summary>
/// Retrieves leave entitlement overrides, optionally filtered by employee, leave type, and year.
/// </summary>
public sealed record GetLeaveEntitlementOverridesQuery(
    Guid? EmployeeId,
    Guid? LeaveTypeId,
    int? LeaveYear
) : IRequest<Result<IReadOnlyList<LeaveEntitlementOverrideDto>>>;
