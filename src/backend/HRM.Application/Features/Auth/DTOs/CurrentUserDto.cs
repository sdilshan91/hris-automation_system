namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// Current user profile with tenant context.
/// </summary>
public sealed record CurrentUserDto
{
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public TenantDto Tenant { get; init; } = null!;
    public IReadOnlyList<string> Roles { get; init; } = [];
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public bool MfaEnabled { get; init; }
}
