using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Commands;

/// <summary>
/// Soft-deletes a per-employee leave entitlement override.
/// </summary>
public sealed record DeleteLeaveEntitlementOverrideCommand(Guid OverrideId) : IRequest<Result>;
