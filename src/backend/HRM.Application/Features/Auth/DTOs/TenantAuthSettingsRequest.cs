namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// Request payload for updating tenant MFA policy settings.
/// </summary>
public sealed record TenantAuthSettingsRequest
{
    public string MfaPolicy { get; init; } = "off";
    public List<string> MfaRequiredRoles { get; init; } = [];
}
