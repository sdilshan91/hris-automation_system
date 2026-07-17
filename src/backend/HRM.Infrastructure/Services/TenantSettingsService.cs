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
        // BUG-288: the leave year is a stored int LABEL on leave_ledger, and the label→date mapping is derived
        // from THIS column. Changing it retroactively reinterprets every historical row: a row written under a
        // January basis carries label 2026, but an April basis resolves a Jan–Mar date to 2025 — so that
        // employee's Jan–Mar accruals/usage silently drop out of the balance they belong to. (Apr–Dec dates are
        // unaffected, which makes the corruption PARTIAL and easy to mistake for a data oddity.)
        //
        // Until ISSUE-305 this was harmless — the column was read by NOTHING, so flipping it was a genuine
        // no-op and nothing needed to guard it. CAL-8 made it load-bearing for balances/accrual/expiry/pro-rata
        // and F&F money, so the write path has to catch up: once a tenant has leave history, their leave-year
        // basis is FROZEN. Set it at provisioning (or before the first accrual runs).
        //
        // Only a CHANGE is rejected. This is a full-replace PUT (BUG-117/ISSUE-310 class): an admin editing
        // their address resends the same month, and that must stay a no-op.
        if (tenant.FiscalYearStartMonth != request.FiscalYearStartMonth)
        {
            // Tenant-scoped by the EF global query filter — this asks "does THIS tenant have leave history".
            var hasLeaveHistory = await _db.LeaveLedgerEntries.AnyAsync(cancellationToken);
            if (hasLeaveHistory)
            {
                return Result<OrgProfileDto>.Failure(
                    $"The fiscal year start month cannot be changed from {tenant.FiscalYearStartMonth} to "
                    + $"{request.FiscalYearStartMonth} because this organization already has leave history. "
                    + "The leave year defines how existing leave-ledger entries are grouped, so changing it now "
                    + "would silently re-date those entries and alter employees' balances. Contact support to "
                    + "migrate the leave year.",
                    422, "fiscal_year_locked_by_leave_history");
            }
        }

        tenant.FiscalYearStartMonth = request.FiscalYearStartMonth;
        // ISSUE-304: the probation period the reminder sweep resolves (a Location may override it).
        tenant.ProbationPeriodDays = request.ProbationPeriodDays;
        // Multi-country tax foundation: normalize the default tax country to upper-case (matches StatutoryRule.CountryCode).
        tenant.DefaultCountryCode = string.IsNullOrWhiteSpace(request.DefaultCountryCode)
            ? null
            : request.DefaultCountryCode.Trim().ToUpperInvariant();
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

        // BUG-005: validate the remaining localization fields, not just the language. Previously any
        // date-format token, time zone, or currency string was persisted verbatim.
        if (!SupportedDateFormats.IsSupported(request.DateFormat))
            return Result<LocalizationDto>.Failure(
                $"Unsupported date format '{request.DateFormat}'. Supported: {string.Join(", ", SupportedDateFormats.Formats)}.",
                400);

        if (!TimeZoneInfo.TryFindSystemTimeZoneById((request.TimeZone ?? string.Empty).Trim(), out _))
            return Result<LocalizationDto>.Failure(
                $"Unknown time zone '{request.TimeZone}'. Expected an IANA time-zone id (e.g. 'Asia/Colombo').",
                400);

        if (!IsoCurrencyCodes.IsValid(request.Currency))
            return Result<LocalizationDto>.Failure(
                $"Unknown currency '{request.Currency}'. Expected a valid ISO-4217 code (e.g. 'USD').",
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

    // ── Update hiring settings (US-REC-010 FR-5/BR-7, ISSUE-140) ──────────────

    public async Task<Result<HiringSettingsDto>> UpdateHiringSettingsAsync(
        UpdateHiringSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var tenant = await LoadCurrentTenantAsync(cancellationToken);
        if (tenant is null)
            return Result<HiringSettingsDto>.Failure("Tenant not found.", 404);

        var before = ToHiringSettingsDto(tenant);

        tenant.AutoCreateUserOnHire = request.AutoCreateUserOnHire;
        tenant.UpdatedAt = DateTime.UtcNow;

        var after = ToHiringSettingsDto(tenant);
        await PersistAsync(tenant, "tenant_settings.hiring_updated", before, after, cancellationToken);

        return Result<HiringSettingsDto>.Success(after);
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
        ToOrgProfileDto(t), ToBrandingDto(t), ToLocalizationDto(t), ToPasswordPolicyDto(t), ToSessionPolicyDto(t),
        t.AutoCreateUserOnHire);

    private static OrgProfileDto ToOrgProfileDto(Tenant t) => new(
        t.Name, t.LegalName, t.RegistrationNumber, t.Address, t.Industry, t.CompanySize,
        t.FiscalYearStartMonth, t.DefaultCountryCode, t.ProbationPeriodDays);

    private static BrandingDto ToBrandingDto(Tenant t) => new(
        t.LogoUrl, t.EmailLogoUrl, t.FaviconUrl, t.PrimaryColor);

    private static LocalizationDto ToLocalizationDto(Tenant t) => new(
        t.DefaultLanguage, t.DateFormat, t.NumberFormat, t.TimeZone, t.Currency);

    private static PasswordPolicyDto ToPasswordPolicyDto(Tenant t) => new(
        t.MinPasswordLength, t.RequireUppercase, t.RequireLowercase, t.RequireDigit,
        t.RequireSpecialCharacter, t.PasswordHistoryCount, t.PasswordMaxAgeDays);

    private static SessionPolicyDto ToSessionPolicyDto(Tenant t) => new(
        t.IdleTimeoutMinutes, t.AbsoluteTimeoutHours, t.MaxConcurrentSessions, t.ConcurrentSessionStrategy);

    private static HiringSettingsDto ToHiringSettingsDto(Tenant t) => new(t.AutoCreateUserOnHire);
}
