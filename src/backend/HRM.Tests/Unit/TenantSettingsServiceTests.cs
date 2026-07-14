using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Security;
using HRM.Application.Features.TenantSettings.DTOs;
using HRM.Domain.Entities;
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
