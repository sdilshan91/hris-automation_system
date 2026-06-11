using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Command to admin-unlock a locked user account (US-AUTH-010 AC-5).
/// BR-3: Admin may only unlock users with a membership in their tenant.
/// </summary>
public sealed record UnlockUserCommand(
    Guid UserId,
    Guid TenantId,
    Guid AdminUserId) : IRequest<Result>;
