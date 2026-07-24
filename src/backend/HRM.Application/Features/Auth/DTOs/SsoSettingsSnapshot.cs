namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// US-AUTH-012 FR-8/NFR-1: the per-tenant SSO configuration in the shape consumed by the login/callback path
/// (US-AUTH-013 isolation, US-AUTH-014 matching/JIT). Served cache-aside per tenant and invalidated on write so
/// the callback does not pay a DB round-trip per login. Contains NO secret material (client secret/cert are
/// platform-level, not per-tenant).
/// </summary>
public sealed record SsoSettingsSnapshot
{
    public bool SsoEnabled { get; init; }
    public List<string> AllowedEntraTenantIds { get; init; } = [];
    public List<string> AllowedEmailDomains { get; init; } = [];
    public bool JitEnabled { get; init; }
    public string? JitDefaultRole { get; init; }
    public string EnforcementMode { get; init; } = "optional";

    /// <summary>
    /// US-AUTH-016 FR-1/FR-2 (NFR-4): the designated break-glass admin user ids, carried on the cached snapshot so
    /// the login path can decide both the <c>sso_only</c> refusal and the break-glass allow with no extra DB hit.
    /// </summary>
    public List<string> BreakGlassAdminUserIds { get; init; } = [];
}
