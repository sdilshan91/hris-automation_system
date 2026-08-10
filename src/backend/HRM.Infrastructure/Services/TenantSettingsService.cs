using System.Text.Json;
using System.Text.RegularExpressions;
using HRM.Application.Common.Helpers;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Common.Security;
using HRM.Application.Features.TenantSettings.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.ValueObjects;
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

        var plan = await ResolvePlanGatingAsync(tenant, cancellationToken);
        return Result<TenantSettingsDto>.Success(ToDto(tenant, plan));
    }

    // ── Update org profile (AC-1) ─────────────────────────────────────────────

    public async Task<Result<OrgProfileDto>> UpdateOrgProfileAsync(
        UpdateOrgProfileRequest request, CancellationToken cancellationToken = default)
    {
        var tenant = await LoadCurrentTenantAsync(cancellationToken);
        if (tenant is null)
            return Result<OrgProfileDto>.Failure("Tenant not found.", 404);

        // ISSUE-229 (BR-4): validate the payslip sender address BEFORE any mutation. Blank/null clears it; a
        // non-blank value must be a well-formed email (reuses the Email value object) else 400. On success we keep
        // the value object's normalized (trimmed, lower-cased) form.
        var payrollFromEmail = Normalize(request.PayrollFromEmail);
        if (payrollFromEmail is not null)
        {
            if (!Email.TryCreate(payrollFromEmail, out var validFromEmail))
                return Result<OrgProfileDto>.Failure(
                    "The payroll sender email address is not a valid email.", 400);
            payrollFromEmail = validFromEmail!.Value;
        }

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
        // DF-20/ISSUE-044 (US-LV-010 FR-7): the tenant leave-cancellation notice window read at cancel time.
        tenant.LeaveCancellationWindowDays = request.LeaveCancellationWindowDays;

        // ISSUE-159 (BR-3): tenant payslip footer disclaimer. Blank clears it → renderer default fallback.
        tenant.PayslipFooterDisclaimer = Normalize(request.PayslipFooterDisclaimer);
        // ISSUE-229 (BR-4): tenant payslip sender "From" address (normalized/validated above). Blank clears it →
        // payslip distribution falls back to the system default sender.
        tenant.PayrollFromEmail = payrollFromEmail;
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
        // GAP-011: null = leave unchanged (see UpdateHiringSettingsRequest).
        if (request.PublicCareersEnabled.HasValue)
            tenant.PublicCareersEnabled = request.PublicCareersEnabled.Value;
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

        // US-ADM-006 BR-3 (ISSUE-358): custom branding colour is a WhiteLabel-plan feature. The frontend has
        // always disabled the picker when the plan locks it, but nothing enforced it server-side — BR-3's
        // "rejected by the API" was unimplemented, so a direct API call bypassed the entitlement entirely.
        // Fails open (see ResolvePlanGatingAsync): an unreadable plan never locks a tenant out of their own
        // branding.
        var gating = await ResolvePlanGatingAsync(tenant, cancellationToken);
        if (gating is not null && gating.LockedFeatures.Contains("branding.customColor"))
            return Result<BrandingDto>.Failure(
                "A custom brand colour requires a plan that includes white-labelling.", 403, "plan_feature_locked");

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

        // ISSUE-358: logo / email-logo / favicon are WhiteLabel-entitled, exactly like the brand colour. Until
        // now only the colour was enforced, so a tenant without the entitlement could still put their own logo
        // across the product AND onto generated payslip/report PDFs — most of what white-labelling actually is.
        //
        // GRANDFATHERED BY DESIGN: this blocks NEW uploads only. Branding already in place keeps rendering after
        // a downgrade, matching how the colour gate behaves and avoiding a billing change silently rebranding a
        // live workspace (and its historical documents, which resolve branding at render time). Clearing an
        // asset back to the default is never blocked — giving a feature up must always be allowed.
        //
        // Fails open (see ResolvePlanGatingAsync): unreadable flags never lock a tenant out of their own brand.
        var gating = await ResolvePlanGatingAsync(tenant, cancellationToken);
        var featureKey = BrandingFeatureKey(kind);
        if (gating is not null && gating.LockedFeatures.Contains(featureKey))
            return Result<BrandingUploadResultDto>.Failure(
                "Custom branding requires a plan that includes white-labelling.", 403, "plan_feature_locked");

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

    // ── Serve branding asset (ISSUE-204) ──────────────────────────────────────

    public async Task<Result<BrandingAssetContentDto>> GetBrandingAssetAsync(
        BrandingAssetKind kind, CancellationToken cancellationToken = default)
    {
        var tenant = await LoadCurrentTenantAsync(cancellationToken);
        if (tenant is null)
            return Result<BrandingAssetContentDto>.Failure("Tenant not found.", 404);

        var storedPath = kind switch
        {
            BrandingAssetKind.Logo => tenant.LogoUrl,
            BrandingAssetKind.EmailLogo => tenant.EmailLogoUrl,
            BrandingAssetKind.Favicon => tenant.FaviconUrl,
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(storedPath))
            return Result<BrandingAssetContentDto>.Failure("No branding asset uploaded.", 404);

        // The stored form is the IFileStorage return value ("/{tenantId}/branding/{file}"); OpenReadAsync
        // re-prefixes the tenant id, so strip the leading "/{tenantId}/" back to the relative path.
        var relativePath = BrandingAssetUrls.ToStorageRelativePath(storedPath, tenant.Id);

        await using var stream = await _fileStorage.OpenReadAsync(tenant.Id, relativePath, cancellationToken);
        if (stream is null)
            return Result<BrandingAssetContentDto>.Failure("Branding asset not found.", 404);

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        var contentType = BrandingAssetUrls.ContentTypeFromExtension(relativePath);
        return Result<BrandingAssetContentDto>.Success(new BrandingAssetContentDto(buffer.ToArray(), contentType));
    }

    // ── Serve a specific tenant's LOGO by subdomain (DF-29) ───────────────────

    public async Task<Result<BrandingAssetContentDto>> GetPublicTenantLogoAsync(
        string subdomain, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subdomain))
            return Result<BrandingAssetContentDto>.Failure("Tenant not found.", 404);

        var normalized = subdomain.Trim().ToLowerInvariant();

        // The ONLY query-filter bypass permitted by this method: resolve the tenant by EXACT subdomain match,
        // exactly as TenantResolutionMiddleware does. Reserved/system subdomains can never be persisted as a
        // tenant (ProvisionTenantValidator forbids them), so an unknown/reserved subdomain simply misses here.
        // We read ONLY the id + logo path — nothing else from the cross-tenant row is exposed.
        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => !t.IsDeleted)
            .Where(t => t.Subdomain == normalized)
            .Select(t => new { t.Id, t.LogoUrl })
            .FirstOrDefaultAsync(cancellationToken);

        if (tenant is null || string.IsNullOrWhiteSpace(tenant.LogoUrl))
            return Result<BrandingAssetContentDto>.Failure("Tenant logo not found.", 404);

        var relativePath = BrandingAssetUrls.ToStorageRelativePath(tenant.LogoUrl, tenant.Id);

        // Cross-tenant by design: pass the RESOLVED tenant id explicitly, NOT the ambient _tenantContext.
        await using var stream = await _fileStorage.OpenReadAsync(tenant.Id, relativePath, cancellationToken);
        if (stream is null)
            return Result<BrandingAssetContentDto>.Failure("Tenant logo not found.", 404);

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        var contentType = BrandingAssetUrls.ContentTypeFromExtension(relativePath);
        return Result<BrandingAssetContentDto>.Success(new BrandingAssetContentDto(buffer.ToArray(), contentType));
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

    private static TenantSettingsDto ToDto(Tenant t, PlanGatingDto? plan = null) => new(
        ToOrgProfileDto(t), ToBrandingDto(t), ToLocalizationDto(t), ToPasswordPolicyDto(t), ToSessionPolicyDto(t),
        t.AutoCreateUserOnHire, plan);

    /// <summary>
    /// US-ADM-006 BR-3 (ISSUE-358): resolves which settings this tenant's plan does NOT permit.
    ///
    /// <para>Only <c>WhiteLabel</c> is evaluated. It is the sole <c>PlanFeatureFlags</c> member with a real
    /// feature behind it (tenant branding); <c>CustomDomain</c>/<c>Scim</c>/<c>Sandbox</c> have zero
    /// implementing code, so gating them would enforce entitlement to nothing.</para>
    ///
    /// <para>FAILS OPEN by design: no plan row, or no flags, means nothing is locked. A tenant must never lose
    /// access to a setting because their plan could not be read — the same fail-open ethos as
    /// <c>PlanModules.IsModuleEnabled</c> and <c>ModuleEntitlementMiddleware</c> (ISSUE-335).</para>
    /// </summary>
    /// <summary>
    /// The branding capabilities WhiteLabel entitles. Every one of these puts the tenant's own identity in
    /// front of users — including on generated payslip and report PDFs, which read branding at render time.
    ///
    /// <para>Gating only the colour (as this originally did) left the other three ungated on every plan, so a
    /// tenant without the entitlement could still replace the logo and favicon across the product. That is
    /// most of what "white-label" means commercially, and it left the flag as the same theatre ISSUE-358
    /// filed it as.</para>
    /// </summary>
    private static readonly string[] WhiteLabelFeatureKeys =
    [
        "branding.customColor",
        "branding.logo",
        "branding.emailLogo",
        "branding.favicon",
    ];

    /// <summary>Maps an upload kind onto the feature key that entitles it.</summary>
    private static string BrandingFeatureKey(BrandingAssetKind kind) => kind switch
    {
        BrandingAssetKind.Logo => "branding.logo",
        BrandingAssetKind.EmailLogo => "branding.emailLogo",
        BrandingAssetKind.Favicon => "branding.favicon",
        _ => "branding.logo",
    };

    private async Task<PlanGatingDto?> ResolvePlanGatingAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        // DELIBERATELY reads the plan row rather than ITenantContext.FeatureFlags, even though the middleware
        // has already resolved those per request and this is therefore one extra indexed lookup.
        //
        // The contract is that a NULL flag set means "unknown ⇒ fail open" while an EMPTY-but-non-null set is
        // authoritative "nothing enabled". Any context that is not fully populated — a background/system
        // context, a job, a test double, a future ITenantContext that returns empty instead of null — would
        // therefore LOCK A PAYING TENANT OUT OF THEIR OWN BRANDING. That is precisely the outcome the fail-open
        // ethos exists to prevent, and one query is a cheap price for not risking it. (The redundancy was
        // flagged as a residual; the tidier version carries the worse failure mode.)
        if (string.IsNullOrWhiteSpace(tenant.PlanId))
            return null;

        var plan = await _db.SubscriptionPlans
            .AsNoTracking()
            .Where(p => p.Code == tenant.PlanId)
            .Select(p => new { p.Code, p.FeatureFlags })
            .FirstOrDefaultAsync(cancellationToken);

        if (plan is null)
            return null;

        // Route through the SHARED derivation + predicate rather than reading FeatureFlags.WhiteLabel directly.
        // PlanFeatureFlagKeys exists so the entitlement seams cannot drift, and this was the one seam not using
        // it — it happened to agree, which is exactly how drift stays invisible until it doesn't.
        var flags = PlanFeatureFlagKeys.Derive(plan.FeatureFlags);

        var locked = PlanFeatureFlagKeys.IsFeatureEnabled(flags, PlanFeatureFlagKeys.WhiteLabel)
            ? new List<string>()
            : [.. WhiteLabelFeatureKeys];

        return new PlanGatingDto(plan.Code, locked);
    }

    private static OrgProfileDto ToOrgProfileDto(Tenant t) => new(
        t.Name, t.LegalName, t.RegistrationNumber, t.Address, t.Industry, t.CompanySize,
        t.FiscalYearStartMonth, t.DefaultCountryCode, t.ProbationPeriodDays, t.PayslipFooterDisclaimer,
        t.PayrollFromEmail, t.LeaveCancellationWindowDays);

    private static BrandingDto ToBrandingDto(Tenant t) => new(
        t.LogoUrl, t.EmailLogoUrl, t.FaviconUrl, t.PrimaryColor);

    private static LocalizationDto ToLocalizationDto(Tenant t) => new(
        t.DefaultLanguage, t.DateFormat, t.NumberFormat, t.TimeZone, t.Currency);

    private static PasswordPolicyDto ToPasswordPolicyDto(Tenant t) => new(
        t.MinPasswordLength, t.RequireUppercase, t.RequireLowercase, t.RequireDigit,
        t.RequireSpecialCharacter, t.PasswordHistoryCount, t.PasswordMaxAgeDays);

    private static SessionPolicyDto ToSessionPolicyDto(Tenant t) => new(
        t.IdleTimeoutMinutes, t.AbsoluteTimeoutHours, t.MaxConcurrentSessions, t.ConcurrentSessionStrategy);

    private static HiringSettingsDto ToHiringSettingsDto(Tenant t) => new(t.AutoCreateUserOnHire, t.PublicCareersEnabled);
}
