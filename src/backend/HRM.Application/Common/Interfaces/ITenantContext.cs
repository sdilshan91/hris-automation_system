using HRM.Domain.Entities;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Scoped service that holds the resolved tenant context for the current request.
/// Populated by TenantResolutionMiddleware.
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }
    string Subdomain { get; }
    TenantStatus Status { get; }
    string? Plan { get; }
    IReadOnlyCollection<string> EnabledModules { get; }
    string? LogoUrl { get; }
    string? PrimaryColor { get; }
    bool IsSystemContext { get; }
    bool IsResolved { get; }

    void SetTenant(
        Guid tenantId,
        string subdomain,
        TenantStatus status,
        string? plan = null,
        IReadOnlyCollection<string>? enabledModules = null,
        string? logoUrl = null,
        string? primaryColor = null);

    void SetSystemContext();
}
