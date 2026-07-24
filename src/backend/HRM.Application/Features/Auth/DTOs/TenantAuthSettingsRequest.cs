namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// Request payload for updating tenant auth policy settings (MFA + session + lockout + SSO policies).
/// US-AUTH-009 FR-1: Session policy fields added.
/// US-AUTH-010 FR-3: Lockout policy fields added.
/// US-AUTH-012 FR-1/FR-2: SSO configuration fields added.
/// </summary>
public sealed record TenantAuthSettingsRequest
{
    public string MfaPolicy { get; init; } = "off";
    public List<string> MfaRequiredRoles { get; init; } = [];

    // Session policy settings (US-AUTH-009 FR-1)
    public int? IdleTimeoutMinutes { get; init; }
    public int? AbsoluteTimeoutHours { get; init; }
    public int? MaxConcurrentSessions { get; init; }
    public string? ConcurrentSessionStrategy { get; init; }

    // Lockout policy settings (US-AUTH-010 FR-3)
    public int? MaxFailedAttempts { get; init; }
    public int? LockoutDurationMinutes { get; init; }
    public bool? ProgressiveLockoutEnabled { get; init; }

    // ── SSO settings (US-AUTH-012 FR-1) ───────────────────────────────────────
    // All nullable: a null field means "leave unchanged". A provided (non-null) list REPLACES the stored one
    // (send [] to clear). The presence of ANY non-null SSO field marks the request as an SSO write, which is
    // gated on the plan's SSO entitlement (FR-3) and audited (FR-7).

    /// <summary>Enable/disable Entra SSO for the tenant (BR-1).</summary>
    public bool? SsoEnabled { get; init; }

    /// <summary>Trusted Entra directory (tenant) IDs — each must be a well-formed GUID (FR-4).</summary>
    public List<string>? AllowedEntraTenantIds { get; init; }

    /// <summary>Verified email domains trusted for SSO — each must be a syntactically valid domain (FR-4).</summary>
    public List<string>? AllowedEmailDomains { get; init; }

    /// <summary>Enable just-in-time provisioning for allow-listed users.</summary>
    public bool? JitEnabled { get; init; }

    /// <summary>Default role for JIT-provisioned users — must exist and be non-privileged (FR-6/BR-5).</summary>
    public string? JitDefaultRole { get; init; }

    /// <summary>Sign-in enforcement mode: "optional" | "sso_only" (FR-5/BR-6).</summary>
    public string? EnforcementMode { get; init; }

    /// <summary>
    /// US-AUTH-016 FR-2/FR-3 (AC-3/BR-1): the designated break-glass admin user ids (GUIDs). Null = leave
    /// unchanged; a provided list REPLACES the stored one (send [] to clear). Each id must resolve to an active
    /// member of this tenant with a password AND an admin/owner role (validated in the service). At least one
    /// valid designation is required to enable <c>sso_only</c>.
    /// </summary>
    public List<string>? BreakGlassAdminUserIds { get; init; }
}
