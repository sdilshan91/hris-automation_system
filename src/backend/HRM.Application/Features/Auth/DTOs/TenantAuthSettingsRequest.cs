namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// Request payload for updating tenant auth policy settings (MFA + session + lockout policies).
/// US-AUTH-009 FR-1: Session policy fields added.
/// US-AUTH-010 FR-3: Lockout policy fields added.
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
}
