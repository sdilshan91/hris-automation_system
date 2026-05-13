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
    bool IsSystemContext { get; }
    bool IsResolved { get; }

    void SetTenant(Guid tenantId, string subdomain, TenantStatus status);
    void SetSystemContext();
}
