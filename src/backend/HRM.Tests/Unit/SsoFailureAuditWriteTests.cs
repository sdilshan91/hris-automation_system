// ============================================================================
// ISSUE-328 (US-AUTH-011 FR-8) — the SSO audit seam on AuthService.
//
// Two concerns, both against the REAL AuthService over EF InMemory (audit rows
// are plain inserts, so the defect — a missing/mis-attributed insert — reproduces
// provider-independently; mirrors AuthAuditWriteTests):
//
//  1) RecordSsoFailureAsync writes a STRUCTURED audit row with the AC-named event
//     and the CORRECT tenant attribution:
//       - a TRUSTED subdomain (from the signed state, as for token-validation
//         failures) → attributed to that tenant (TC-AUTH-148),
//       - a null subdomain (state-invalid) → a SYSTEM-LEVEL row (null TenantId),
//         even when an ambient tenant context is resolved (TC-AUTH-144),
//       - an UNKNOWN subdomain → system-level (never fabricate a tenant).
//
//  2) SsoSignInAsync writes the AC-named SUCCESS event `sso_login_succeeded`
//     (renamed from `sso_login`; the write site was its only consumer) — TC-AUTH-140.
//
// Removing/renaming/mis-attributing the audit write fails the relevant arm.
// ============================================================================

using FluentAssertions;
using Hangfire;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Auth.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Identity;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace HRM.Tests.Unit;

[Trait("TC", "TC-AUTH-148")]
public sealed class SsoFailureAuditWriteTests
{
    private const string Subdomain = "acme";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;

    public SsoFailureAuditWriteTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "hrm-api-test",
                ["Jwt:Audience"] = "hrm-client-test",
                ["Platform:BaseDomain"] = "yourhrm.test",
            })
            .Build();

        _jwtService = new JwtService(_configuration);
    }

    // ── 1) RecordSsoFailureAsync tenant attribution ───────────────────────────

    [Fact]
    public async Task RecordSsoFailure_WithTrustedSubdomain_AttributesToResolvedTenant()
    {
        // token-validation failures pass parsedState.Subdomain (trusted) → the row is attributed to that tenant.
        await SeedTenantAsync();

        await CreateService().RecordSsoFailureAsync(
            "sso_token_validation_failed", Subdomain, "nonce_mismatch", "203.0.113.20", "xUnit", default);

        using var db = CreateDbContext();
        var rows = db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EventType == "sso_token_validation_failed")
            .ToList();

        rows.Should().HaveCount(1, "the token-validation failure must be recorded in the tenant audit trail");
        rows[0].TenantId.Should().Be(_tenantId, "a failure with a trusted subdomain is attributed to that HRM tenant");
        rows[0].UserId.Should().BeNull("no user is authenticated at a token-validation failure");
    }

    [Fact]
    [Trait("TC", "TC-AUTH-144")]
    public async Task RecordSsoFailure_WithNullSubdomain_WritesSystemLevelRow_EvenUnderResolvedContext()
    {
        // state-invalid: the subdomain cannot be trusted, so a SYSTEM-LEVEL (null TenantId) row is written — and
        // it must NOT fall back to the ambient _tenantContext (which here resolves to _tenantId). This pins the
        // "written directly, not via the context-falling-back helper" decision.
        await SeedTenantAsync();

        await CreateService().RecordSsoFailureAsync(
            "sso_state_invalid", null, "state_could_not_be_validated", "203.0.113.21", "xUnit", default);

        using var db = CreateDbContext();
        var rows = db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EventType == "sso_state_invalid")
            .ToList();

        rows.Should().HaveCount(1, "a state-invalid failure must still be recorded");
        rows[0].TenantId.Should().BeNull(
            "an unverified state must never be attributed to a tenant — not even the ambient context's");
    }

    [Fact]
    public async Task RecordSsoFailure_WithUnknownSubdomain_WritesSystemLevelRow()
    {
        // A subdomain that resolves to no tenant must NOT fabricate a tenant → system-level.
        await SeedTenantAsync();

        await CreateService().RecordSsoFailureAsync(
            "sso_token_validation_failed", "ghost-workspace", "id_token_validation_failed", null, null, default);

        using var db = CreateDbContext();
        var row = db.AuditLogs.IgnoreQueryFilters()
            .Single(a => a.EventType == "sso_token_validation_failed");

        row.TenantId.Should().BeNull("an unknown subdomain must not be fabricated into a tenant attribution");
    }

    // ── 2) SsoSignInAsync success rename: sso_login -> sso_login_succeeded ─────

    [Fact]
    [Trait("TC", "TC-AUTH-140")]
    public async Task SsoSignIn_Success_Writes_SsoLoginSucceeded_NotLegacyName()
    {
        // A fully-authorized SSO sign-in (user matched by Entra oid, active membership) must audit the AC-named
        // success event `sso_login_succeeded`.
        var oid = Guid.NewGuid().ToString();
        await SeedSsoUserAsync(oid);

        var identity = new SsoIdentity
        {
            Subdomain = Subdomain,
            TenantId = Guid.NewGuid().ToString(), // Entra directory id (tid) — for audit context only
            ObjectId = oid,
            Email = "user@acme.test",
            DisplayName = "Acme User",
            JitAllowed = false,
            IpAddress = "203.0.113.22",
            UserAgent = "xUnit",
        };

        var result = await CreateService().SsoSignInAsync(identity, default);
        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().NotBeNullOrWhiteSpace();

        using var db = CreateDbContext();
        db.AuditLogs.IgnoreQueryFilters()
            .Count(a => a.EventType == "sso_login_succeeded" && a.UserId == _userId && a.TenantId == _tenantId)
            .Should().Be(1, "a successful SSO login must be recorded under the AC-named event (TC-AUTH-140)");
        db.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "sso_login")
            .Should().BeFalse("the legacy `sso_login` name was renamed to `sso_login_succeeded`");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private AuthService CreateService() => new(
        CreateDbContext(),
        _jwtService,
        _tenantContext,
        Substitute.For<ITotpService>(),
        _configuration,
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
        Substitute.For<ILogger<AuthService>>(),
        Substitute.For<IBackgroundJobClient>());

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private async Task SeedTenantAsync()
    {
        using var db = CreateDbContext();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Acme",
            Subdomain = Subdomain,
            Status = TenantStatus.Active,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedSsoUserAsync(string oid)
    {
        using var db = CreateDbContext();

        var role = new Role
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            Name = PermissionCatalog.BuiltInRoles.Employee,
            CreatedAt = DateTime.UtcNow,
        };

        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Acme",
            Subdomain = Subdomain,
            Status = TenantStatus.Active,
            CreatedAt = DateTime.UtcNow,
        });

        db.Users.Add(new User
        {
            Id = _userId,
            Email = "user@acme.test",
            DisplayName = "Acme User",
            PasswordHash = null,
            IsActive = true,
            IdentityProvider = "entra",
            EntraObjectId = oid,
            CreatedAt = DateTime.UtcNow,
        });

        db.Roles.Add(role);

        var membership = new UserTenant
        {
            Id = BaseEntity.NewUuidV7(),
            UserId = _userId,
            TenantId = _tenantId,
            Status = UserTenantStatus.Active,
            CreatedAt = DateTime.UtcNow,
        };
        membership.UserTenantRoles.Add(new UserTenantRole
        {
            UserTenantId = membership.Id,
            RoleId = role.Id,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = "unit-test",
        });
        db.UserTenants.Add(membership);

        await db.SaveChangesAsync();
    }
}
