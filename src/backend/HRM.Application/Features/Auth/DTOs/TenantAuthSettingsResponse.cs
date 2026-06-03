namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// Response containing the tenant's current MFA policy settings.
/// </summary>
public sealed record TenantAuthSettingsResponse
{
    public string MfaPolicy { get; init; } = "off";
    public List<string> MfaRequiredRoles { get; init; } = [];
}
