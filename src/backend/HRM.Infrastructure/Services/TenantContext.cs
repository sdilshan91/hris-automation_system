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
    public string? Plan { get; private set; }
    public IReadOnlyCollection<string> EnabledModules { get; private set; } = Array.Empty<string>();
    public string? LogoUrl { get; private set; }
    public string? PrimaryColor { get; private set; }
    public bool IsSystemContext { get; private set; }
    public bool IsResolved { get; private set; }

    public void SetTenant(
        Guid tenantId,
        string subdomain,
        TenantStatus status,
        string? plan = null,
        IReadOnlyCollection<string>? enabledModules = null,
        string? logoUrl = null,
        string? primaryColor = null)
    {
        TenantId = tenantId;
        Subdomain = subdomain;
        Status = status;
        Plan = plan;
        EnabledModules = enabledModules ?? Array.Empty<string>();
        LogoUrl = logoUrl;
        PrimaryColor = primaryColor;
        IsSystemContext = false;
        IsResolved = true;
    }

    public void SetSystemContext()
    {
        TenantId = Guid.Empty;
        Subdomain = "admin";
        Status = TenantStatus.Active;
        Plan = "system";
        EnabledModules = Array.Empty<string>();
        LogoUrl = null;
        PrimaryColor = null;
        IsSystemContext = true;
        IsResolved = true;
    }
}
