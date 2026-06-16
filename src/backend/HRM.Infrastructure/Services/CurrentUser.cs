using System.Security.Claims;
using HRM.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Scoped service that extracts the current user from JWT claims.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public Guid UserId =>
        Guid.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("sub"), out var id)
            ? id
            : Guid.Empty;

    public string Email =>
        User?.FindFirstValue(ClaimTypes.Email) ?? User?.FindFirstValue("email") ?? string.Empty;

    public Guid TenantId =>
        Guid.TryParse(User?.FindFirstValue("tenant_id"), out var id) ? id : Guid.Empty;

    public Guid UserTenantId =>
        Guid.TryParse(User?.FindFirstValue("user_tenant_id"), out var id) ? id : Guid.Empty;

    public IReadOnlyList<string> Roles =>
        User?.FindAll("roles").Select(c => c.Value).ToList() ?? [];

    public IReadOnlyList<string> Permissions =>
        User?.FindAll("permissions").Select(c => c.Value).ToList() ?? [];

    // ── Impersonation (US-ADM-003) ──

    public bool IsImpersonating =>
        string.Equals(User?.FindFirstValue(ImpersonationClaims.IsImpersonation), "true", StringComparison.OrdinalIgnoreCase);

    public Guid? ImpersonatorId =>
        Guid.TryParse(User?.FindFirstValue(ImpersonationClaims.ActorId), out var id) ? id : null;

    public Guid? ImpersonationSessionId =>
        Guid.TryParse(User?.FindFirstValue(ImpersonationClaims.SessionId), out var id) ? id : null;

    public bool ImpersonationReadOnly =>
        string.Equals(User?.FindFirstValue(ImpersonationClaims.ReadOnly), "true", StringComparison.OrdinalIgnoreCase);
}
