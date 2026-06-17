using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Users.Commands;

/// <summary>
/// US-ADM-005 AC-5: force a password reset for a user — revokes ALL refresh tokens for the user across ALL
/// tenants (the password is global), nulls PasswordChangedAt, dispatches a reset email, audits. The
/// membership is resolved tenant-scoped first (a Tenant A admin cannot target a user with no tenant-A
/// membership), but the token revocation + password change are deliberately cross-tenant by UserId.
/// </summary>
public sealed record ForcePasswordResetCommand(Guid UserTenantId) : IRequest<Result>;

public sealed class ForcePasswordResetCommandHandler : IRequestHandler<ForcePasswordResetCommand, Result>
{
    private readonly IUserManagementService _service;

    public ForcePasswordResetCommandHandler(IUserManagementService service) => _service = service;

    public Task<Result> Handle(ForcePasswordResetCommand request, CancellationToken cancellationToken)
        => _service.ForcePasswordResetAsync(request.UserTenantId, cancellationToken);
}
