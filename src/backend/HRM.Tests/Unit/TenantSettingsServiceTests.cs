using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Security;
using HRM.Application.Features.TenantSettings.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace HRM.Tests.Unit;

/// <summary>
/// US-ADM-006: resolved-tenant InMemory tests for the Tenant-Admin company-settings service. Each test runs in
/// the NORMAL resolved-tenant context (TenantId set, IsResolved = true). The Tenant row is loaded strictly by
/// ITenantContext.TenantId, so isolation (AC-5) is proven by seeding two tenants and asserting a Tenant A
/// update never touches Tenant B.
/// </summary>
public sealed class TenantSettingsServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    public TenantSettingsServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);
        _currentUser.TenantId.Returns(_tenantId);
        _currentUser.IsAuthenticated.Returns(true);
    }

    // ── GET + Update org profile (AC-1, NFR-4) ───────────────────────────────

    [Fact]
    public async Task UpdateOrgProfile_PersistsValues_AndWritesBeforeAfterAudit()
    {
        await SeedTenantAsync(_tenantId, name: "Old Co");
        var service = CreateService();

        var result = await service.UpdateOrgProfileAsync(new UpdateOrgProfileRequest(
            Name: "New Co",
            LegalName: "New Co Pvt Ltd",
            RegistrationNumber: "REG-123",
            Address: "1 Main St",
            Industry: "Software",
            CompanySize: "11-50",
            FiscalYearStartMonth: 4,
            DefaultCountryCode: "lk")); // lower-case in → normalized upper-case out (multi-country tax).

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("New Co");
        result.Value.FiscalYearStartMonth.Should().Be(4);
        result.Value.DefaultCountryCode.Should().Be("LK"); // round-trips + normalized.

        using var db = CreateDbContext();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == _tenantId);
        tenant.Name.Should().Be("New Co");
        tenant.LegalName.Should().Be("New Co Pvt Ltd");
        tenant.RegistrationNumber.Should().Be("REG-123");
        tenant.FiscalYearStartMonth.Should().Be(4);

        var audit = await db.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.EventType == "tenant_settings.org_profile_updated");
        audit.TenantId.Should().Be(_tenantId);
        audit.UserId.Should().Be(_userId);
        audit.Before.Should().Contain("Old Co");
        audit.After.Should().Contain("New Co");
    }

    [Fact]
    public async Task GetSettings_ReturnsCurrentTenantSnapshot()
    {
        await SeedTenantAsync(_tenantId, name: "Acme");
        var service = CreateService();

        var result = await service.GetSettingsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.OrgProfile.Name.Should().Be("Acme");
        result.Value.PasswordPolicy.MinLength.Should().Be(12);
        result.Value.Localization.DefaultLanguage.Should().Be("en");
    }

    // ── ISSUE-159: the payslip footer disclaimer round-trips through save → GET ──
    // Regression for the write/read asymmetry: the value is persisted and consumed by the payslip
    // renderer, but ToOrgProfileDto originally dropped it, so the settings GET always returned null.
    [Fact]
    [Trait("TC", "TC-PAY-018")]
    public async Task UpdateOrgProfile_PayslipFooterDisclaimer_RoundTripsThroughGet()
    {
        await SeedTenantAsync(_tenantId, name: "Acme");
        var service = CreateService();

        var update = await service.UpdateOrgProfileAsync(new UpdateOrgProfileRequest(
            Name: "Acme",
            LegalName: null, RegistrationNumber: null, Address: null,
            Industry: null, CompanySize: null, FiscalYearStartMonth: 1, DefaultCountryCode: null,
            PayslipFooterDisclaimer: "Confidential — payroll use only."));

        // The update result itself echoes the saved value...
        update.IsSuccess.Should().BeTrue(update.Error);
        update.Value!.PayslipFooterDisclaimer.Should().Be("Confidential — payroll use only.");

        // ...and so does a fresh GET (the ToOrgProfileDto read path — null before the fix).
        var snapshot = await service.GetSettingsAsync();
        snapshot.Value!.OrgProfile.PayslipFooterDisclaimer.Should().Be("Confidential — payroll use only.");
    }

    // ── Localization (AC-3, FR-4) ────────────────────────────────────────────

    [Fact]
    public async Task UpdateLocalization_ValidSet_Persists()
    {
        await SeedTenantAsync(_tenantId);
        var service = CreateService();

        var result = await service.UpdateLocalizationAsync(new UpdateLocalizationRequest(
            DefaultLanguage: "si",
            DateFormat: "dd/MM/yyyy",
            NumberFormat: "1.234,56",
            TimeZone: "Asia/Colombo",
            Currency: "lkr"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.DefaultLanguage.Should().Be("si");
        result.Value.Currency.Should().Be("LKR");

        using var db = CreateDbContext();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == _tenantId);
        tenant.DefaultLanguage.Should().Be("si");
        tenant.DateFormat.Should().Be("dd/MM/yyyy");
        tenant.TimeZone.Should().Be("Asia/Colombo");
    }

    [Fact]
    public async Task UpdateLocalization_UnknownLanguage_Rejected()
    {
        await SeedTenantAsync(_tenantId);
        var service = CreateService();

        var result = await service.UpdateLocalizationAsync(new UpdateLocalizationRequest(
            DefaultLanguage: "zz",
            DateFormat: "dd/MM/yyyy",
            NumberFormat: "1,234.56",
            TimeZone: "UTC",
            Currency: "USD"));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("Unsupported language");
    }

    // ── Password policy (AC-4 Test Hint) ─────────────────────────────────────

    [Fact]
    public async Task UpdatePasswordPolicy_Persists_AndEnforcesMinLengthViaValidator()
    {
        await SeedTenantAsync(_tenantId);
        var service = CreateService();

        var result = await service.UpdatePasswordPolicyAsync(new UpdatePasswordPolicyRequest(
            MinLength: 12,
            RequireUppercase: true,
            RequireLowercase: true,
            RequireDigit: true,
            RequireSpecialCharacter: true,
            HistoryCount: 5,
            MaxAgeDays: 90));

        result.IsSuccess.Should().BeTrue();
        result.Value!.MinLength.Should().Be(12);
        result.Value.MaxAgeDays.Should().Be(90);

        // AC-4 Test Hint: under a min-length=12 policy, a 10-char password is rejected by the validation path.
        using var db = CreateDbContext();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == _tenantId);
        var policy = new PasswordPolicy(
            tenant.MinPasswordLength, tenant.RequireUppercase, tenant.RequireLowercase,
            tenant.RequireDigit, tenant.RequireSpecialCharacter, tenant.PasswordHistoryCount, tenant.PasswordMaxAgeDays);

        PasswordPolicyValidator.IsValid("Ab1@cdefgh", policy).Should().BeFalse(); // 10 chars → too short
        PasswordPolicyValidator.Validate("Ab1@cdefgh", policy)
            .Should().ContainSingle(e => e.Contains("at least 12 characters"));
        PasswordPolicyValidator.IsValid("Ab1@cdefghij", policy).Should().BeTrue(); // 12 chars, all classes
    }

    // ── Session policy (FR-6) ────────────────────────────────────────────────

    [Fact]
    public async Task UpdateSessionPolicy_Persists()
    {
        await SeedTenantAsync(_tenantId);
        var service = CreateService();

        var result = await service.UpdateSessionPolicyAsync(new UpdateSessionPolicyRequest(
            IdleTimeoutMinutes: 30,
            AbsoluteTimeoutHours: 8,
            MaxConcurrentSessions: 3,
            ConcurrentSessionStrategy: "deny_new"));

        result.IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == _tenantId);
        tenant.IdleTimeoutMinutes.Should().Be(30);
        tenant.AbsoluteTimeoutHours.Should().Be(8);
        tenant.MaxConcurrentSessions.Should().Be(3);
        tenant.ConcurrentSessionStrategy.Should().Be("deny_new");
    }

    // ── Hiring settings (US-REC-010 FR-5/BR-7, ISSUE-140) ────────────────────

    [Fact]
    public async Task UpdateHiringSettings_Persists_AndWritesBeforeAfterAudit()
    {
        await SeedTenantAsync(_tenantId);
        var service = CreateService();

        // Default is false; flip it on.
        var result = await service.UpdateHiringSettingsAsync(new UpdateHiringSettingsRequest(AutoCreateUserOnHire: true));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.AutoCreateUserOnHire.Should().BeTrue();

        using var db = CreateDbContext();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == _tenantId);
        tenant.AutoCreateUserOnHire.Should().BeTrue("the toggle must be persisted");

        var audit = await db.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.EventType == "tenant_settings.hiring_updated");
        audit.TenantId.Should().Be(_tenantId);
        audit.UserId.Should().Be(_userId);
        audit.Before.Should().Contain("false"); // before: default off
        audit.After.Should().Contain("true");   // after: on

        // And it round-trips through the GET snapshot.
        var snapshot = await service.GetSettingsAsync();
        snapshot.Value!.AutoCreateUserOnHire.Should().BeTrue();
    }

    // ── Primary color (FR-3) ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdatePrimaryColor_InvalidHex_Rejected()
    {
        await SeedTenantAsync(_tenantId);
        var service = CreateService();

        var result = await service.UpdatePrimaryColorAsync("not-a-color");

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task UpdatePrimaryColor_ValidHex_Stored()
    {
        await SeedTenantAsync(_tenantId);
        var service = CreateService();

        var result = await service.UpdatePrimaryColorAsync("#4F46E5");

        result.IsSuccess.Should().BeTrue();
        result.Value!.PrimaryColor.Should().Be("#4F46E5");

        using var db = CreateDbContext();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == _tenantId);
        tenant.PrimaryColor.Should().Be("#4F46E5");
    }

    // ── Branding upload (AC-2 / NFR-2) ───────────────────────────────────────

    [Fact]
    public async Task UploadBranding_ValidPng_AcceptedAndLogoUrlUpdated()
    {
        await SeedTenantAsync(_tenantId);
        var service = CreateService();

        var result = await service.UploadBrandingAsync(BrandingAssetKind.Logo, ValidPngBytes(), "logo.png");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Url.Should().NotBeNullOrWhiteSpace();

        using var db = CreateDbContext();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == _tenantId);
        tenant.LogoUrl.Should().Be(result.Value.Url);
        tenant.LogoUrl.Should().Contain("branding/logo.png");
    }

    [Fact]
    public async Task UploadBranding_PngExtensionButWrongMagicBytes_Rejected()
    {
        await SeedTenantAsync(_tenantId);
        var service = CreateService();

        // Bytes that are NOT a real PNG even though the caller named it ".png".
        var fakePng = new byte[] { 0x42, 0x4D, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };

        var result = await service.UploadBrandingAsync(BrandingAssetKind.Logo, fakePng, "logo.png");

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("Unsupported file type");

        using var db = CreateDbContext();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == _tenantId);
        tenant.LogoUrl.Should().BeNull();
    }

    [Fact]
    public async Task UploadBranding_OversizeFile_Rejected()
    {
        await SeedTenantAsync(_tenantId);
        var service = CreateService();

        // Valid PNG header but larger than the 2 MB logo cap.
        var oversize = new byte[BrandingFileValidator.LogoMaxBytes + 1];
        var header = ValidPngBytes();
        Array.Copy(header, oversize, header.Length);

        var result = await service.UploadBrandingAsync(BrandingAssetKind.Logo, oversize, "logo.png");

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("maximum size");
    }

    // ── ISSUE-229 (BR-4): tenant payslip "From" address round-trips + is validated ──
    [Fact]
    [Trait("TC", "TC-PAY-019")]
    public async Task UpdateOrgProfile_PayrollFromEmail_RoundTripsThroughGet()
    {
        await SeedTenantAsync(_tenantId, name: "Acme");
        var service = CreateService();

        var update = await service.UpdateOrgProfileAsync(new UpdateOrgProfileRequest(
            Name: "Acme",
            LegalName: null, RegistrationNumber: null, Address: null,
            Industry: null, CompanySize: null, FiscalYearStartMonth: 1, DefaultCountryCode: null,
            PayrollFromEmail: "payroll@acme.test"));

        update.IsSuccess.Should().BeTrue(update.Error);
        update.Value!.PayrollFromEmail.Should().Be("payroll@acme.test");

        // ...and it round-trips through the GET read path (not write-only — the ISSUE-159 lesson).
        // Fresh service → fresh DbContext, so this is a true cross-context read of the persisted value.
        var snapshot = await CreateService().GetSettingsAsync();
        snapshot.Value!.OrgProfile.PayrollFromEmail.Should().Be("payroll@acme.test");
    }

    [Fact]
    [Trait("TC", "TC-PAY-019")]
    public async Task UpdateOrgProfile_InvalidPayrollFromEmail_Is400_AndNotPersisted()
    {
        await SeedTenantAsync(_tenantId, name: "Acme");
        var service = CreateService();

        var result = await service.UpdateOrgProfileAsync(new UpdateOrgProfileRequest(
            Name: "Acme",
            LegalName: null, RegistrationNumber: null, Address: null,
            Industry: null, CompanySize: null, FiscalYearStartMonth: 1, DefaultCountryCode: null,
            PayrollFromEmail: "not-an-email"));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);

        using var db = CreateDbContext();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == _tenantId);
        tenant.PayrollFromEmail.Should().BeNull("a rejected invalid address must not be persisted");
    }

    // ── DF-29: cross-tenant public logo by subdomain (tenant switcher) ────────
    // The switcher lists OTHER tenants, so GetPublicTenantLogoAsync resolves a tenant by SUBDOMAIN
    // (IgnoreQueryFilters, like TenantResolutionMiddleware) and streams ONLY that tenant's public logo.

    [Fact]
    public async Task GetPublicTenantLogo_ByOtherTenantSubdomain_StreamsThatTenantsLogo()
    {
        var tenantB = Guid.NewGuid();
        await SeedTenantAsync(_tenantId, name: "Tenant A");                        // caller context = A
        await SeedTenantWithLogoAsync(tenantB, subdomain: "beta", logoFile: "logo.png");

        // Service runs in Tenant A's context but must resolve Tenant B's PUBLIC logo by subdomain (by design).
        var storage = new SeededLogoFileStorage(new byte[] { 1, 2, 3, 4 });
        var service = new TenantSettingsService(
            CreateDbContext(), _tenantContext, _currentUser, storage,
            Substitute.For<ILogger<TenantSettingsService>>(), cache: null);

        var result = await service.GetPublicTenantLogoAsync("beta");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Content.Should().Equal(1, 2, 3, 4);
        result.Value.ContentType.Should().Be("image/png");
        // ISOLATION-CRITICAL: the read must use the RESOLVED tenant (B), NOT the ambient caller (A).
        // Guards the "pass the resolved id, not _tenantContext" invariant — otherwise it would read A's partition.
        storage.LastReadTenantId.Should().Be(tenantB).And.NotBe(_tenantId);
    }

    [Fact]
    public async Task GetPublicTenantLogo_SubdomainMatchIsCaseInsensitive()
    {
        var tenantB = Guid.NewGuid();
        await SeedTenantWithLogoAsync(tenantB, subdomain: "beta", logoFile: "logo.png");
        var service = new TenantSettingsService(
            CreateDbContext(), _tenantContext, _currentUser,
            new SeededLogoFileStorage(new byte[] { 9 }),
            Substitute.For<ILogger<TenantSettingsService>>(), cache: null);

        (await service.GetPublicTenantLogoAsync("BETA")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetPublicTenantLogo_UnknownSubdomain_Is404()
    {
        await SeedTenantAsync(_tenantId);
        var service = CreateService();

        var result = await service.GetPublicTenantLogoAsync("does-not-exist");

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetPublicTenantLogo_TenantWithoutLogo_Is404()
    {
        var tenantB = Guid.NewGuid();
        await SeedTenantAsync(tenantB, name: "No-Logo Co"); // seeded with no LogoUrl
        // subdomain of SeedTenantAsync is t-{guid}; look it up.
        using (var db = CreateDbContext())
        {
            var sub = (await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenantB)).Subdomain;
            var service = CreateService();
            var result = await service.GetPublicTenantLogoAsync(sub);
            result.IsFailure.Should().BeTrue();
            result.StatusCode.Should().Be(404);
        }
    }

    // ── Isolation (AC-5) ─────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateOrgProfile_AsTenantA_DoesNotTouchTenantB()
    {
        var tenantB = Guid.NewGuid();
        await SeedTenantAsync(_tenantId, name: "Tenant A");
        await SeedTenantAsync(tenantB, name: "Tenant B");

        var service = CreateService(); // context is Tenant A

        await service.UpdateOrgProfileAsync(new UpdateOrgProfileRequest(
            Name: "Tenant A Renamed",
            LegalName: null, RegistrationNumber: null, Address: null,
            Industry: null, CompanySize: null, FiscalYearStartMonth: 1, DefaultCountryCode: null));

        using var db = CreateDbContext();
        var a = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == _tenantId);
        var b = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenantB);

        a.Name.Should().Be("Tenant A Renamed");
        b.Name.Should().Be("Tenant B"); // untouched

        var bAudits = await db.AuditLogs.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantB);
        bAudits.Should().Be(0);
    }

    // ── Cache invalidation graceful no-op (FR-7) ─────────────────────────────

    [Fact]
    public async Task UpdateOrgProfile_NoDistributedCache_NoOpsCleanly()
    {
        await SeedTenantAsync(_tenantId);
        var service = CreateService(cache: null); // no IDistributedCache registered

        var result = await service.UpdateOrgProfileAsync(new UpdateOrgProfileRequest(
            Name: "Cacheless Co",
            LegalName: null, RegistrationNumber: null, Address: null,
            Industry: null, CompanySize: null, FiscalYearStartMonth: 1, DefaultCountryCode: null));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Cacheless Co");
    }

    [Fact]
    public async Task UpdateOrgProfile_WithDistributedCache_EvictsConfigKey()
    {
        await SeedTenantAsync(_tenantId);
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        await cache.SetStringAsync($"t:{_tenantId}:config", "cached-value");

        var service = CreateService(cache);

        await service.UpdateOrgProfileAsync(new UpdateOrgProfileRequest(
            Name: "Evicting Co",
            LegalName: null, RegistrationNumber: null, Address: null,
            Industry: null, CompanySize: null, FiscalYearStartMonth: 1, DefaultCountryCode: null));

        (await cache.GetStringAsync($"t:{_tenantId}:config")).Should().BeNull();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private TenantSettingsService CreateService(IDistributedCache? cache = null)
    {
        return new TenantSettingsService(
            CreateDbContext(),
            _tenantContext,
            _currentUser,
            new InMemoryFileStorage(),
            Substitute.For<ILogger<TenantSettingsService>>(),
            cache);
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    // ══ BUG-288: the leave-year basis is FROZEN once leave history exists ══════════════════════════
    //
    // The leave year is a stored int LABEL on leave_ledger and the label→date mapping comes from
    // Tenant.FiscalYearStartMonth. Changing it retroactively re-dates every historical row: a Jan–Mar row
    // written under a January basis (label 2026) is resolved to 2025 under an April basis, so it silently
    // drops out of the employee's balance. Apr–Dec dates are unaffected, so the corruption is PARTIAL — which
    // is what makes it read as a data oddity rather than a config error.
    //
    // Until ISSUE-305 the column was read by NOTHING, so flipping it was a genuine no-op. CAL-8 made it
    // load-bearing (balances/accrual/expiry/pro-rata/F&F money) and the write path had to catch up.

    /// <summary>
    /// The provisioning path stays OPEN: a tenant with no leave history can still choose its basis. This is how
    /// a real Apr–Mar tenant is onboarded, so a guard that blocked it would defeat the epic.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ADM-288")]
    public async Task FiscalYearStartMonth_CanBeSet_WhenTheTenantHasNoLeaveHistory()
    {
        await SeedTenantAsync(_tenantId);
        var service = CreateService();

        var result = await service.UpdateOrgProfileAsync(OrgProfile(fiscalYearStartMonth: 4));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.FiscalYearStartMonth.Should().Be(4);
    }

    /// <summary>
    /// THE guard: once the tenant has a single leave-ledger row, changing the basis is rejected 422 rather
    /// than silently re-dating that row.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ADM-288")]
    public async Task FiscalYearStartMonth_CannotBeChanged_OnceLeaveHistoryExists()
    {
        await SeedTenantAsync(_tenantId);
        await SeedLeaveLedgerRowAsync(_tenantId);
        var service = CreateService();

        var result = await service.UpdateOrgProfileAsync(OrgProfile(fiscalYearStartMonth: 4));

        result.IsSuccess.Should().BeFalse("changing the basis would re-date existing ledger rows");
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("fiscal_year_locked_by_leave_history");

        using var db = CreateDbContext();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == _tenantId);
        tenant.FiscalYearStartMonth.Should().Be(1, "the rejected change must not have been persisted");
    }

    /// <summary>
    /// ⚠ THE ARM A NAIVE GUARD BREAKS. This is a FULL-REPLACE PUT (BUG-117/ISSUE-310 class): an admin editing
    /// the address resends every field, including the UNCHANGED fiscal month. If the guard fired on
    /// "field present" rather than "value changed", every org-profile edit would 422 for any tenant with leave
    /// history — i.e. all of them.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ADM-288")]
    public async Task UnrelatedProfileEdits_StillWork_WhenTheFiscalMonthIsResent_Unchanged()
    {
        await SeedTenantAsync(_tenantId, name: "Old Co");
        await SeedLeaveLedgerRowAsync(_tenantId);
        var service = CreateService();

        // Same month as seeded (1) — the admin is only changing the address.
        var result = await service.UpdateOrgProfileAsync(
            OrgProfile(fiscalYearStartMonth: 1, name: "New Co", address: "2 New St"));

        result.IsSuccess.Should().BeTrue(
            "resending the SAME basis is a no-op — the lock must trigger on a CHANGE, not on the field's presence");
        result.Value!.Name.Should().Be("New Co");
        result.Value.Address.Should().Be("2 New St");
    }

    /// <summary>
    /// Tenant isolation (Critical Rule #1): the guard asks "does THIS tenant have leave history" via the EF
    /// global query filter. Tenant B's ledger must never lock Tenant A — an unfiltered `AnyAsync()` would lock
    /// every tenant in the system as soon as any one of them accrued leave.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ADM-288")]
    public async Task AnotherTenantsLeaveHistory_DoesNotLockThisTenant()
    {
        var otherTenant = Guid.NewGuid();
        await SeedTenantAsync(_tenantId);
        await SeedTenantAsync(otherTenant, name: "Other Co");
        await SeedLeaveLedgerRowAsync(otherTenant);   // history belongs to SOMEONE ELSE
        var service = CreateService();

        var result = await service.UpdateOrgProfileAsync(OrgProfile(fiscalYearStartMonth: 4));

        result.IsSuccess.Should().BeTrue(
            "this tenant has no leave history of its own; another tenant's rows must be invisible here");
        result.Value!.FiscalYearStartMonth.Should().Be(4);
    }

    private static UpdateOrgProfileRequest OrgProfile(
        int fiscalYearStartMonth, string name = "Test Co", string? address = null)
        => new(
            Name: name,
            LegalName: null,
            RegistrationNumber: null,
            Address: address,
            Industry: null,
            CompanySize: null,
            FiscalYearStartMonth: fiscalYearStartMonth,
            DefaultCountryCode: null);

    private async Task SeedLeaveLedgerRowAsync(Guid tenantId)
    {
        using var db = CreateDbContext();
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            EntryType = LedgerEntryType.Accrual,
            EmployeeId = Guid.NewGuid(),
            LeaveTypeId = Guid.NewGuid(),
            LeaveYear = 2026,
            Amount = 14m,
            BalanceAfter = 14m,
            OccurredAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedTenantAsync(Guid tenantId, string name = "Test Tenant")
    {
        using var db = CreateDbContext();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Subdomain = $"t-{tenantId:N}".Substring(0, 12),
            Name = name,
            Status = TenantStatus.Active,
            PlanId = "default",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    // DF-29: a tenant with an explicit subdomain + a stored logo path (the cross-tenant switcher target).
    private async Task SeedTenantWithLogoAsync(Guid tenantId, string subdomain, string logoFile)
    {
        using var db = CreateDbContext();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Subdomain = subdomain,
            Name = subdomain,
            Status = TenantStatus.Active,
            PlanId = "default",
            LogoUrl = $"/{tenantId}/branding/{logoFile}",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>IFileStorage whose OpenReadAsync returns fixed bytes, so the cross-tenant logo READ path
    /// is exercised (the default InMemoryFileStorage returns null on read). Captures the tenantId it was
    /// read with so a test can prove the RESOLVED tenant (not the ambient caller) drives the read.</summary>
    private sealed class SeededLogoFileStorage : IFileStorage
    {
        private readonly byte[] _bytes;
        public Guid? LastReadTenantId { get; private set; }
        public SeededLogoFileStorage(byte[] bytes) => _bytes = bytes;

        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
        {
            LastReadTenantId = tenantId;
            return Task.FromResult<Stream?>(new MemoryStream(_bytes));
        }

        public Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken cancellationToken = default)
            => Task.FromResult($"/{tenantId}/{relativePath}");

        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null)
            => $"/files/{tenantId}/{relativePath}";

        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static byte[] ValidPngBytes()
    {
        // 8-byte PNG signature + a little filler.
        return new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };
    }

    /// <summary>Minimal in-memory IFileStorage so the upload path is exercised without touching the disk.</summary>
    private sealed class InMemoryFileStorage : IFileStorage
    {
        public Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken cancellationToken = default)
            => Task.FromResult($"/{tenantId}/{relativePath}");

        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(null);

        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null)
            => $"/files/{tenantId}/{relativePath}";

        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
