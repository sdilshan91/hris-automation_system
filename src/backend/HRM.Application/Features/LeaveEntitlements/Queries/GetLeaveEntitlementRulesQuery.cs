using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Queries;

/// <summary>
/// Retrieves leave entitlement rules, optionally filtered by leave type.
/// </summary>
public sealed record GetLeaveEntitlementRulesQuery(Guid? LeaveTypeId)
    : IRequest<Result<IReadOnlyList<LeaveEntitlementRuleDto>>>;
