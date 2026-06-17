using HRM.Application.Common.Models;
using HRM.Application.Common.Security;
using HRM.Application.Features.TenantSettings.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ADM-006: Tenant-Admin company-settings service. Every method operates ONLY on the CURRENT tenant via
/// <see cref="ITenantContext.TenantId"/> — there is no tenant_id parameter, so the operations are inherently
/// isolated (AC-5). Settings are realized as typed columns on the Tenant entity (no EAV table). Each mutating
/// method audits before/after (NFR-4) and evicts the tenant config cache when one is present (FR-7).
/// </summary>
public interface ITenantSettingsService
{
    /// <summary>GET full settings snapshot for the current tenant (FR-1).</summary>
    Task<Result<TenantSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Update organization profile (AC-1): name/legal/registration/address/industry/size/fiscal-year.</summary>
    Task<Result<OrgProfileDto>> UpdateOrgProfileAsync(
        UpdateOrgProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>Update localization defaults (AC-3): language (validated)/date/number/time-zone/currency.</summary>
    Task<Result<LocalizationDto>> UpdateLocalizationAsync(
        UpdateLocalizationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Update the tenant password policy (AC-4/FR-5).</summary>
    Task<Result<PasswordPolicyDto>> UpdatePasswordPolicyAsync(
        UpdatePasswordPolicyRequest request, CancellationToken cancellationToken = default);

    /// <summary>Update the tenant session policy (FR-6): idle/absolute timeout, max concurrent sessions.</summary>
    Task<Result<SessionPolicyDto>> UpdateSessionPolicyAsync(
        UpdateSessionPolicyRequest request, CancellationToken cancellationToken = default);

    /// <summary>Update the primary brand color (FR-3) — a validated hex string; FE derives shades.</summary>
    Task<Result<BrandingDto>> UpdatePrimaryColorAsync(
        string primaryColor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upload a branding asset (AC-2/FR-2/NFR-2). <paramref name="content"/> is the fully buffered bytes;
    /// type is validated by magic bytes and size server-side, then stored under {tenantId}/branding/ and the
    /// resulting URL saved to the corresponding Tenant column (LogoUrl/EmailLogoUrl/FaviconUrl).
    /// </summary>
    Task<Result<BrandingUploadResultDto>> UploadBrandingAsync(
        BrandingAssetKind kind, byte[] content, string originalFileName, CancellationToken cancellationToken = default);
}
