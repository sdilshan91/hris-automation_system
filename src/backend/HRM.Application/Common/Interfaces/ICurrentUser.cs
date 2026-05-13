namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Scoped service representing the currently authenticated user from JWT claims.
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }
    string Email { get; }
    Guid TenantId { get; }
    Guid UserTenantId { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<string> Permissions { get; }
    bool IsAuthenticated { get; }
}
