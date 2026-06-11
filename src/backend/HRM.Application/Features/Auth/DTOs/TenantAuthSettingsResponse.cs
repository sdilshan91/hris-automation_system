namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// Response containing the tenant's current auth policy settings (MFA + session).
/// US-AUTH-009 FR-1: Session policy fields added.
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
}
