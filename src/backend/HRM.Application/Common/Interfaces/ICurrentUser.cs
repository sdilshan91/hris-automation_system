namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Scoped service representing the currently authenticated user from JWT claims.
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }
    string Email { get; }
    Guid TenantId { get; }
    Guid UserTenantId { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<string> Permissions { get; }
    bool IsAuthenticated { get; }

    // ── Impersonation (US-ADM-003) — read from the imp_* / is_impersonation JWT claims ──

    /// <summary>
    /// True when the current token is an impersonation token (the <c>is_impersonation</c> claim is "true").
    /// While impersonating, <see cref="UserId"/>/<see cref="TenantId"/> are the TARGET user/tenant — use
    /// <see cref="ImpersonatorId"/> for the actual operator (the System Admin / System Support user).
    /// </summary>
    bool IsImpersonating { get; }

    /// <summary>The original System Admin / System Support user driving the session (the <c>imp_actor_id</c> claim).</summary>
    Guid? ImpersonatorId { get; }

    /// <summary>The active impersonation session id (the <c>imp_session_id</c> claim).</summary>
    Guid? ImpersonationSessionId { get; }

    /// <summary>True when the impersonation session is read-only (the <c>imp_readonly</c> claim is "true").</summary>
    bool ImpersonationReadOnly { get; }
}
