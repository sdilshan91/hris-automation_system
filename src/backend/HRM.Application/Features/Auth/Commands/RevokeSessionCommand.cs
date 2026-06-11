using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Command to revoke a specific session (US-AUTH-009 AC-5, AC-6).
/// Used by both admin and self-service revocation.
/// </summary>
public sealed record RevokeSessionCommand(
    Guid SessionId,
    Guid UserId,
    Guid TenantId,
    Guid? CurrentSessionId,
    bool IsAdminAction) : IRequest<Result>;
