using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Queries;

/// <summary>
/// Retrieves a single leave entitlement rule by ID.
/// </summary>
public sealed record GetLeaveEntitlementRuleByIdQuery(Guid RuleId)
    : IRequest<Result<LeaveEntitlementRuleDto>>;
