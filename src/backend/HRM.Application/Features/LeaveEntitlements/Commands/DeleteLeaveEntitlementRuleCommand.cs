using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Commands;

/// <summary>
/// Soft-deletes a leave entitlement rule.
/// </summary>
public sealed record DeleteLeaveEntitlementRuleCommand(Guid RuleId) : IRequest<Result>;
