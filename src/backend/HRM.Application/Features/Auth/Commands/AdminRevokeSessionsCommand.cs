using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Command for admin to revoke a specific session or all sessions for a user (US-AUTH-009 AC-5).
/// If SessionId is null, all sessions for the user in the tenant are revoked.
/// </summary>
public sealed record AdminRevokeSessionsCommand(
    Guid TargetUserId,
    Guid TenantId,
    Guid? SessionId) : IRequest<Result>;
