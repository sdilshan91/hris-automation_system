using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Commands;

/// <summary>
/// Bulk-creates leave entitlement rules (US-LV-002 FR-4).
/// </summary>
public sealed record BulkCreateRulesCommand(
    IReadOnlyList<UpsertLeaveEntitlementRuleRequest> Rules
) : IRequest<Result<IReadOnlyList<LeaveEntitlementRuleDto>>>;
