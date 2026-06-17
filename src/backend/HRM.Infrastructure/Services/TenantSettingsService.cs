using System.Text.Json;
using System.Text.RegularExpressions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Common.Security;
using HRM.Application.Features.TenantSettings.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-ADM-006: Tenant-Admin company-settings service. Realizes the story's "tenant_setting keys" as TYPED
/// COLUMNS on the <see cref="Tenant"/> entity (matching the codebase convention — no EAV table).
///
/// <para><b>Isolation (AC-5):</b> the Tenant row is NOT scoped by TenantId via a global query filter (it IS
/// the tenant), so every load goes through <see cref="LoadCurrentTenantAsync"/>, which resolves strictly by
/// <c>_tenantContext.TenantId</c> — there is no tenant_id parameter to manipulate. A Tenant A request can only
/// ever load and mutate Tenant A's row. We deliberately scope by Id rather than IgnoreQueryFilters tricks; the
/// only filter on Tenant is <c>!IsDeleted</c>, which we keep.</para>
///
/// <para><b>Audit (NFR-4):</b> each mutating method writes an AuditLog with before/after JSON in the structured
/// Before/After columns. <b>Cache (FR-7):</b> when an <see cref="IDistributedCache"/> is registered the tenant
/// config key <c>t:{tenantId}:config</c> is evicted; when none is present (Redis not wired on this platform)
/// it no-ops gracefully (the dependency is optional/nullable).</para>
/// </summary>
public sealed class TenantSettingsService : ITenantSettingsService
{
    private static readonly Regex HexColorRegex = new("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _fileStorage;
    private readonly IDistributedCache? _cache;
    private readonly ILogger<TenantSettingsService> _logger;

    public TenantSettingsService(
        AppDbContext db,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IFileStorage fileStorage,
        ILogger<TenantSettingsService> logger,
        IDistributedCache? cache = null)
    {
        _db = db;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
        _logger = logger;
        _cache = cache;
    }

    // ── GET settings (FR-1) ───────────────────────────────────────────────────

    public async Task<Result<TenantSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await LoadCurrentTenantAsync(cancellationToken);
        if (tenant is null)
            return Result<TenantSettingsDto>.Failure("Tenant not found.", 404);

        return Result<TenantSettingsDto>.Success(ToDto(tenant));
    }

    // ── Update org profile (AC-1) ─────────────────────────────────────────────

    public async Task<Result<OrgProfileDto>> UpdateOrgProfileAsync(
        UpdateOrgProfileRequest request, CancellationToken cancellationToken = default)
    {
        var tenant = await LoadCurrentTenantAsync(cancellationToken);
        if (tenant is null)
            return Result<OrgProfileDto>.Failure("Tenant not found.", 404);

        var before = ToOrgProfileDto(tenant);

        tenant.Name = request.Name.Trim();
        tenant.LegalName = Normalize(request.LegalName);
        tenant.RegistrationNumber = Normalize(request.RegistrationNumber);
        tenant.Address = Normalize(request.Address);
        tenant.Industry = Normalize(request.Industry);
        tenant.CompanySize = Normalize(request.CompanySize);
        tenant.FiscalYearStartMonth = request.FiscalYearStartMonth;
        tenant.UpdatedAt = DateTime.UtcNow;

        var after = ToOrgProfileDto(tenant);
        await PersistAsync(tenant, "tenant_settings.org_profile_updated", before, after, cancellationToken);

        return Result<OrgProfileDto>.Success(after);
    }

    // ── Update localization (AC-3, FR-4, BR-5) ────────────────────────────────

    public async Task<Result<LocalizationDto>> UpdateLocalizationAsync(
        UpdateLocalizationRequest request, CancellationToken cancellationToken = default)
    {
        if (!SupportedLanguages.IsSupported(request.DefaultLanguage))
            return Result<LocalizationDto>.Failure(
                $"Unsupported language '{request.DefaultLanguage}'. Supported: {string.Join(", ", SupportedLanguages.Codes)}.",
                400);

        var tenant = await LoadCurrentTenantAsync(cancellationToken);
        if (tenant is null)
            return Result<LocalizationDto>.Failure("Tenant not found.", 404);

        var before = ToLocalizationDto(tenant);

        tenant.DefaultLanguage = request.DefaultLanguage.Trim().ToLowerInvariant();
        tenant.DateFormat = request.DateFormat.Trim();
        tenant.NumberFormat = request.NumberFormat.Trim();
        tenant.TimeZone = request.TimeZone.Trim();
        tenant.Currency = request.Currency.Trim().ToUpperInvariant();
        tenant.UpdatedAt = DateTime.UtcNow;

        var after = ToLocalizationDto(tenant);
        await PersistAsync(tenant, "tenant_settings.localization_updated", before, after, cancellationToken);

        return Result<LocalizationDto>.Success(after);
    }

    // ── Update password policy (AC-4/FR-5) ────────────────────────────────────

    public async Task<Result<PasswordPolicyDto>> UpdatePasswordPolicyAsync(
        UpdatePasswordPolicyRequest request, CancellationToken cancellationToken = default)
    {
        var tenant = await LoadCurrentTenantAsync(cancellationToken);
        if (tenant is null)
            return Result<PasswordPolicyDto>.Failure("Tenant not found.", 404);

        var before = ToPasswordPolicyDto(tenant);

        tenant.MinPasswordLength = request.MinLength;
        tenant.RequireUppercase = request.RequireUppercase;
        tenant.RequireLowercase = request.RequireLowercase;
        tenant.RequireDigit = request.RequireDigit;
        tenant.RequireSpecialCharacter = request.RequireSpecialCharacter;
        tenant.PasswordHistoryCount = request.HistoryCount;
        tenant.PasswordMaxAgeDays = request.MaxAgeDays;
        tenant.UpdatedAt = DateTime.UtcNow;

        var after = ToPasswordPolicyDto(tenant);
        await PersistAsync(tenant, "tenant_settings.password_policy_updated", before, after, cancellationToken);

        return Result<PasswordPolicyDto>.Success(after);
    }

    // ── Update session policy (FR-6) ──────────────────────────────────────────

    public async Task<Result<SessionPolicyDto>> UpdateSessionPolicyAsync(
        UpdateSessionPolicyRequest request, CancellationToken cancellationToken = default)
    {
        var tenant = await LoadCurrentTenantAsync(cancellationToken);
        if (tenant is null)
            return Result<SessionPolicyDto>.Failure("Tenant not found.", 404);

        var before = ToSessionPolicyDto(tenant);

        tenant.IdleTimeoutMinutes = request.IdleTimeoutMinutes;
        tenant.AbsoluteTimeoutHours = request.AbsoluteTimeoutHours;
        tenant.MaxConcurrentSessions = request.MaxConcurrentSessions;
        if (!string.IsNullOrWhiteSpace(request.ConcurrentSessionStrategy))
            tenant.ConcurrentSessionStrategy = request.ConcurrentSessionStrategy.Trim();
        tenant.UpdatedAt = DateTime.UtcNow;

        var after = ToSessionPolicyDto(tenant);
        await PersistAsync(tenant, "tenant_settings.session_policy_updated", before, after, cancellationToken);

        return Result<SessionPolicyDto>.Success(after);
    }

    // ── Update primary color (FR-3) ───────────────────────────────────────────

    public async Task<Result<BrandingDto>> UpdatePrimaryColorAsync(
        string primaryColor, CancellationToken cancellationToken = default)
    {
        var value = primaryColor?.Trim() ?? string.Empty;
        if (!HexColorRegex.IsMatch(value))
            return Result<BrandingDto>.Failure("Primary color must be a valid hex color (e.g. #4F46E5).", 400);

        var tenant = await LoadCurrentTenantAsync(cancellationToken);
        if (tenant is null)
            return Result<BrandingDto>.Failure("Tenant not found.", 404);

        var before = ToBrandingDto(tenant);
        tenant.PrimaryColor = value;
        tenant.UpdatedAt = DateTime.UtcNow;

        var after = ToBrandingDto(tenant);
        await PersistAsync(tenant, "tenant_settings.primary_color_updated", before, after, cancellationToken);

        return Result<BrandingDto>.Success(after);
    }

    // ── Upload branding asset (AC-2/FR-2/NFR-2/BR-6) ──────────────────────────

    public async Task<Result<BrandingUploadResultDto>> UploadBrandingAsync(
        BrandingAssetKind kind, byte[] content, string originalFileName, CancellationToken cancellationToken = default)
    {
        // NFR-2: real, server-side magic-byte + size validation (extension is ignored).
        var validation = BrandingFileValidator.Validate(kind, content);
        if (validation.IsFailure)
            return Result<BrandingUploadResultDto>.Failure(validation.Error!, validation.StatusCode ?? 400);

        var tenant = await LoadCurrentTenantAsync(cancellationToken);
        if (tenant is null)
            return Result<BrandingUploadResultDto>.Failure("Tenant not found.", 404);

        var fileType = validation.Value!;
        var fileName = kind switch
        {
            BrandingAssetKind.Logo => $"logo{fileType.Extension}",
            BrandingAssetKind.EmailLogo => $"email-logo{fileType.Extension}",
            BrandingAssetKind.Favicon => $"favicon{fileType.Extension}",
            _ => $"asset{fileType.Extension}",
        };

        // BR-6: tenant-scoped storage path {tenantId}/branding/{filename}. IFileStorage already prefixes the
        // tenant id, so we pass the relative "branding/{filename}".
        var relativePath = $"branding/{fileName}";

        string url;
        await using (var stream = new MemoryStream(content, writable: false))
        {
            url = await _fileStorage.UploadAsync(
                tenant.Id, relativePath, stream, fileType.ContentType, cancellationToken);
        }

        var before = ToBrandingDto(tenant);
        switch (kind)
        {
            case BrandingAssetKind.Logo:
                tenant.LogoUrl = url;
                break;
            case BrandingAssetKind.EmailLogo:
                tenant.EmailLogoUrl = url;
                break;
            case BrandingAssetKind.Favicon:
                tenant.FaviconUrl = url;
                break;
        }
        tenant.UpdatedAt = DateTime.UtcNow;

        var after = ToBrandingDto(tenant);
        await PersistAsync(tenant, "tenant_settings.branding_uploaded", before, after, cancellationToken);

        return Result<BrandingUploadResultDto>.Success(new BrandingUploadResultDto(kind.ToString(), url));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the CURRENT tenant's row strictly by <c>_tenantContext.TenantId</c> (AC-5). Never accepts a
    /// tenant id, so cross-tenant access is impossible. Returns null when unresolved or not found.
    /// </summary>
    private async Task<Tenant?> LoadCurrentTenantAsync(CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return null;

        var tenantId = _tenantContext.TenantId;
        return await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
    }

    /// <summary>Saves the tenant + writes a before/after audit row, then evicts the config cache (FR-7).</summary>
    private async Task PersistAsync(
        Tenant tenant, string eventType, object before, object after, CancellationToken cancellationToken)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenant.Id,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = eventType,
            Action = eventType,
            ResourceType = "Tenant",
            ResourceId = tenant.Id.ToString(),
            Before = JsonSerializer.Serialize(before),
            After = JsonSerializer.Serialize(after),
            CreatedAt = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync(cancellationToken);
        await InvalidateConfigCacheAsync(tenant.Id, cancellationToken);

        _logger.LogInformation("Tenant {TenantId} settings change: {EventType}", tenant.Id, eventType);
    }

    /// <summary>
    /// FR-7: evict <c>t:{tenantId}:config</c>. No-ops gracefully when no IDistributedCache is registered
    /// (Redis is not wired on this platform). Never hard-depends on Redis.
    /// </summary>
    private async Task InvalidateConfigCacheAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (_cache is null)
            return;

        try
        {
            await _cache.RemoveAsync($"t:{tenantId}:config", cancellationToken);
        }
        catch (Exception ex)
        {
            // Cache eviction is best-effort; a cache outage must not fail the settings write.
            _logger.LogWarning(ex, "Failed to evict config cache for tenant {TenantId}", tenantId);
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TenantSettingsDto ToDto(Tenant t) => new(
        ToOrgProfileDto(t), ToBrandingDto(t), ToLocalizationDto(t), ToPasswordPolicyDto(t), ToSessionPolicyDto(t));

    private static OrgProfileDto ToOrgProfileDto(Tenant t) => new(
        t.Name, t.LegalName, t.RegistrationNumber, t.Address, t.Industry, t.CompanySize, t.FiscalYearStartMonth);

    private static BrandingDto ToBrandingDto(Tenant t) => new(
        t.LogoUrl, t.EmailLogoUrl, t.FaviconUrl, t.PrimaryColor);

    private static LocalizationDto ToLocalizationDto(Tenant t) => new(
        t.DefaultLanguage, t.DateFormat, t.NumberFormat, t.TimeZone, t.Currency);

    private static PasswordPolicyDto ToPasswordPolicyDto(Tenant t) => new(
        t.MinPasswordLength, t.RequireUppercase, t.RequireLowercase, t.RequireDigit,
        t.RequireSpecialCharacter, t.PasswordHistoryCount, t.PasswordMaxAgeDays);

    private static SessionPolicyDto ToSessionPolicyDto(Tenant t) => new(
        t.IdleTimeoutMinutes, t.AbsoluteTimeoutHours, t.MaxConcurrentSessions, t.ConcurrentSessionStrategy);
}
