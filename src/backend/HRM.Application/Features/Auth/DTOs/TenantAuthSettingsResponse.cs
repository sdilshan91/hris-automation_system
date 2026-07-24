namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// Response containing the tenant's current auth policy settings (MFA + session + lockout + SSO).
/// US-AUTH-009 FR-1: Session policy fields added.
/// US-AUTH-010 FR-3: Lockout policy fields added.
/// US-AUTH-012 FR-1/FR-2: SSO configuration fields added.
/// </summary>
public sealed record TenantAuthSettingsResponse
{
    public string MfaPolicy { get; init; } = "off";
    public List<string> MfaRequiredRoles { get; init; } = [];

    // Session policy settings (US-AUTH-009 FR-1)
    public int IdleTimeoutMinutes { get; init; } = 60;
    public int AbsoluteTimeoutHours { get; init; } = 24;
    public int MaxConcurrentSessions { get; init; } = 5;
    public string ConcurrentSessionStrategy { get; init; } = "revoke_oldest";

    // Lockout policy settings (US-AUTH-010 FR-3)
    public int MaxFailedAttempts { get; init; } = 5;
    public int LockoutDurationMinutes { get; init; } = 15;
    public bool ProgressiveLockoutEnabled { get; init; }

    // ── SSO settings (US-AUTH-012 FR-1) ───────────────────────────────────────
    public bool SsoEnabled { get; init; }
    public List<string> AllowedEntraTenantIds { get; init; } = [];
    public List<string> AllowedEmailDomains { get; init; } = [];
    public bool JitEnabled { get; init; }
    public string? JitDefaultRole { get; init; }
    public string EnforcementMode { get; init; } = "optional";

    /// <summary>
    /// Whether the tenant's plan includes the SSO entitlement (US-ADM-009 PlanFeatureFlags.Sso). The FE uses this
    /// to show the SSO card enabled vs a disabled "available on higher plans" state (AC-1/AC-2). Read-only.
    /// </summary>
    public bool SsoEntitled { get; init; }
}
