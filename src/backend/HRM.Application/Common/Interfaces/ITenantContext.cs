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

    /// <summary>
    /// D3 (ISSUE-358): the plan feature flags enabled for the resolved tenant (canonical keys, see
    /// <see cref="HRM.Domain.Authorization.PlanFeatureFlagKeys"/>), carried per-request exactly like
    /// <see cref="EnabledModules"/> so a gate costs NO extra query. <c>null</c> is the FAIL-OPEN sentinel:
    /// "no plan resolved / flags unreadable" ⇒ nothing is denied. A non-null (even empty) set is authoritative.
    ///
    /// <para>Shipped as a DEFAULT interface implementation returning <c>null</c> so the many test doubles that
    /// implement this interface keep compiling without change — the same technique used for
    /// <c>IFieldEncryptor.ModelCacheDiscriminator</c>. Those doubles therefore read as fail-open, denying nothing.</para>
    /// </summary>
    IReadOnlyCollection<string>? FeatureFlags => null;

    void SetTenant(
        Guid tenantId,
        string subdomain,
        TenantStatus status,
        string? plan = null,
        IReadOnlyCollection<string>? enabledModules = null,
        string? logoUrl = null,
        string? primaryColor = null);

    /// <summary>
    /// D3 (ISSUE-358): populate the resolved tenant's plan feature flags, set alongside <see cref="SetTenant"/>
    /// during tenant resolution. Default no-op so test doubles that don't care keep compiling (they stay
    /// fail-open). Pass <c>null</c> for "could not resolve the plan" (fail-open); a non-null set is authoritative.
    /// </summary>
    void SetFeatureFlags(IReadOnlyCollection<string>? featureFlags) { }

    void SetSystemContext();
}
