namespace HRM.Application.Features.Auth.DTOs;

public sealed record TenantMembershipDto(
    Guid TenantId,
    string Subdomain,
    string Name,
    string? LogoUrl,
    string Status,
    IReadOnlyList<string> Roles,
    bool IsCurrentTenant);

public sealed record SwitchTenantRequest(Guid TenantId);

public sealed record SwitchTenantResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public TenantDto Tenant { get; init; } = null!;
    public string RedirectUrl { get; init; } = string.Empty;

    // Refresh token is set via cookie, but kept here for internal controller use.
    public string? RefreshToken { get; init; }
}
