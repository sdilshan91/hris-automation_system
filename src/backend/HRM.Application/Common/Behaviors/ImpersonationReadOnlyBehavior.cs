using HRM.Application.Common.Exceptions;
using HRM.Application.Common.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HRM.Application.Common.Behaviors;

/// <summary>
/// US-ADM-003 (AC-5/AC-6/FR-3/FR-6/BR-1): MediatR pipeline behavior that constrains what a request may do while
/// an impersonation session is active. Two independent gates, both returning 403 (via <see cref="ForbiddenException"/>):
///
/// <list type="number">
/// <item><b>Read-only sessions (AC-5/AC-6/BR-1).</b> When <c>ICurrentUser.IsImpersonating &amp;&amp;
/// ImpersonationReadOnly</c>, every WRITE request is blocked. "Write" is detected structurally: the request type
/// name ends in <c>Command</c> (the codebase's CQRS convention — commands mutate, queries don't). A System
/// Support session and a session against a Suspended tenant are both read-only, so this is what makes
/// "Support impersonation is read-only" real.</item>
///
/// <item><b>Destructive operations (FR-6) — blocked even for a FULL (non-read-only) System Admin impersonation.</b>
/// A name-pattern blocklist refuses the highest-risk mutations during ANY impersonation: changing/resetting
/// passwords, mutating roles/permissions, and deleting users or tenants. The list is intentionally conservative
/// (substring match on the request type name) — see <see cref="DestructiveCommandMarkers"/>.</item>
/// </list>
///
/// <para>Non-impersonated requests pass straight through (no behavior change for ordinary traffic). The behavior
/// runs early in the pipeline (registered before the handler in <c>Program.cs</c>), so a blocked write never
/// reaches its handler or touches the database.</para>
/// </summary>
public sealed class ImpersonationReadOnlyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// FR-6 destructive-operation blocklist. A request whose type NAME contains any of these markers
    /// (case-insensitive) is forbidden during impersonation, regardless of read-only status. Substring matching
    /// keeps it robust to command naming (e.g. <c>ChangePasswordCommand</c>, <c>ResetUserPasswordCommand</c>,
    /// <c>AssignRoleCommand</c>, <c>UpdateRolePermissionsCommand</c>, <c>DeleteUserCommand</c>,
    /// <c>DeleteTenantCommand</c>, <c>TerminateTenantCommand</c>).
    /// </summary>
    private static readonly string[] DestructiveCommandMarkers =
    [
        "ChangePassword",
        "ResetPassword",
        "SetPassword",
        "AssignRole",
        "UnassignRole",
        "RemoveRole",
        "Permission",   // role/permission mutations (Update/Grant/RevokePermission…)
        "DeleteUser",
        "DeleteTenant",
        "TerminateTenant",
        // BUG-107: the prior markers missed these user-admin mutations (no substring overlap):
        "ForcePasswordReset",   // ForcePasswordResetCommand (not caught by "ResetPassword")
        "DeactivateUser",       // DeactivateUserCommand (not caught by "DeleteUser")
        "AssignUserRoles",      // AssignUserRolesCommand (not caught by "AssignRole")
        "EditUserRoles",        // EditUserRolesCommand
    ];

    /// <summary>
    /// Commands that MUST remain callable while impersonating even under a read-only session — ending the
    /// session itself (AC-3). Without this allowlist the read-only gate would trap the operator in the session,
    /// since "End Impersonation" is a write command sent with the impersonation token from the tenant UI banner.
    /// </summary>
    private static readonly string[] AlwaysAllowedCommandMarkers =
    [
        "EndImpersonation",
    ];

    private const string ReadOnlyMessage = "Support impersonation is read-only.";
    private const string DestructiveMessage = "This operation is not permitted during impersonation.";

    private readonly ICurrentUser _currentUser;
    private readonly ILogger<ImpersonationReadOnlyBehavior<TRequest, TResponse>> _logger;

    public ImpersonationReadOnlyBehavior(
        ICurrentUser currentUser,
        ILogger<ImpersonationReadOnlyBehavior<TRequest, TResponse>> logger)
    {
        _currentUser = currentUser;
        _logger = logger;
    }

    public Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsImpersonating)
            return next(cancellationToken);

        var requestName = typeof(TRequest).Name;

        // Always allow the operator to end the session, even under a read-only impersonation (AC-3).
        if (AlwaysAllowedCommandMarkers.Any(m => requestName.Contains(m, StringComparison.OrdinalIgnoreCase)))
            return next(cancellationToken);

        // FR-6: highest-risk mutations are blocked even for a full System Admin impersonation.
        if (IsDestructive(requestName))
        {
            _logger.LogWarning(
                "Blocked destructive operation {Request} during impersonation session {SessionId} by actor {ActorId}.",
                requestName, _currentUser.ImpersonationSessionId, _currentUser.ImpersonatorId);
            throw new ForbiddenException(DestructiveMessage);
        }

        // AC-5/AC-6/BR-1: a read-only session blocks every write command.
        if (_currentUser.ImpersonationReadOnly && IsWriteCommand(requestName))
        {
            _logger.LogWarning(
                "Blocked write {Request} under read-only impersonation session {SessionId} by actor {ActorId}.",
                requestName, _currentUser.ImpersonationSessionId, _currentUser.ImpersonatorId);
            throw new ForbiddenException(ReadOnlyMessage);
        }

        return next(cancellationToken);
    }

    private static bool IsWriteCommand(string requestName) =>
        requestName.EndsWith("Command", StringComparison.Ordinal);

    private static bool IsDestructive(string requestName) =>
        DestructiveCommandMarkers.Any(m => requestName.Contains(m, StringComparison.OrdinalIgnoreCase));
}
