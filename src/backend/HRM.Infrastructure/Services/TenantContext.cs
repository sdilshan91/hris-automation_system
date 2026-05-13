using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Scoped service that holds the resolved tenant context for the current request.
/// Set by TenantResolutionMiddleware early in the pipeline.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    public Guid TenantId { get; private set; }
    public string Subdomain { get; private set; } = string.Empty;
    public TenantStatus Status { get; private set; }
    public bool IsSystemContext { get; private set; }
    public bool IsResolved { get; private set; }

    public void SetTenant(Guid tenantId, string subdomain, TenantStatus status)
    {
        TenantId = tenantId;
        Subdomain = subdomain;
        Status = status;
        IsSystemContext = false;
        IsResolved = true;
    }

    public void SetSystemContext()
    {
        IsSystemContext = true;
        IsResolved = true;
    }
}
