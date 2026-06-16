namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ADM-003: the JWT claim names that carry impersonation context. Single source of truth shared by the
/// token minting (<c>JwtService</c>) and the claim reading (<c>CurrentUser</c> / enforcement middleware) so
/// the producer and consumer never drift. <see cref="IsImpersonation"/> already exists on the standard access
/// token (hardcoded "false"); an impersonation token sets it to "true" and adds the <c>imp_*</c> claims.
/// </summary>
public static class ImpersonationClaims
{
    /// <summary>"true" / "false" — already emitted on the standard access token.</summary>
    public const string IsImpersonation = "is_impersonation";

    /// <summary>The impersonation session id (GUID).</summary>
    public const string SessionId = "imp_session_id";

    /// <summary>The original System Admin / System Support user id (GUID).</summary>
    public const string ActorId = "imp_actor_id";

    /// <summary>The reason, truncated for transport.</summary>
    public const string Reason = "imp_reason";

    /// <summary>"true" / "false" — whether the session is read-only.</summary>
    public const string ReadOnly = "imp_readonly";

    /// <summary>The session hard-expiry as a unix-seconds string.</summary>
    public const string ExpiresAt = "imp_expires_at";
}
