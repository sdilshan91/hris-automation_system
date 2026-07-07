// ============================================================================
// ISSUE-049 — a refresh token is bound to its owning tenant and must be rejected
// when presented on a DIFFERENT tenant's resolved subdomain.
//
// AuthService.RefreshTokenAsync now short-circuits with a generic 401 when
// `_tenantContext.IsResolved && storedToken.TenantId != _tenantContext.TenantId`,
// placed BEFORE reuse detection so a cross-tenant presentation can never trigger
// lineage revocation against another tenant's tokens.
//
// Harness mirrors RefreshTokenReuseLineageTests / SessionManagementTests: real
// AuthService over InMemory EF, tokens seeded with a real HashToken hash. The
// service's injected ITenantContext is what selects the "resolved subdomain":
//   - Foreign arm  → context resolved to tenant B → 401 AND the tenant-A token is
//                    NOT revoked (no cross-tenant side effect on its lineage).
//   - Control arm  → context resolved to tenant A → the same token rotates: the
//                    presented token is revoked and a fresh token is persisted.
//
// Why it fails pre-fix: without the tenant-binding check, presenting tenant A's
// token under tenant B falls through to the normal rotation path and SUCCEEDS
// (200) — leaking a cross-tenant refresh. So the 401 assertion fails pre-fix.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
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

public sealed class RefreshTokenCrossTenantRejectTests
{
    private const string Email = "user@acme.com";
    private readonly Guid _tenantAId = Guid.NewGuid();
    private readonly Guid _tenantBId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _tokenId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;

    public RefreshTokenCrossTenantRejectTests()
    {
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "hrm-api-test",
            ["Jwt:Audience"] = "hrm-client-test",
            ["Platform:BaseDomain"] = "yourhrm.test",
        }).Build();
        _jwtService = new JwtService(_configuration);
    }

    // Binds @TC-AUTH-ISO-049.
    [Fact]
    public async Task Refresh_ForeignSubdomain_Rejected_ISSUE049()
    {
        var rawToken = await SeedTenantAWithTokenAsync();

        // Present tenant A's token while the request resolves to tenant B.
        var result = await CreateService(_tenantBId).RefreshTokenAsync(rawToken, "10.0.0.9", "Chrome", default);

        // Rejected with the generic, non-leaking 401 (same message as an unknown token).
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(401);
        result.Error.Should().Be("Invalid refresh token.");

        // No cross-tenant side effect: the token's lineage must be untouched (NOT revoked),
        // proving the check fired BEFORE reuse-detection/rotation.
        using var db = Db();
        var token = await db.RefreshTokens.IgnoreQueryFilters().SingleAsync(t => t.Id == _tokenId);
        token.RevokedAt.Should().BeNull("a cross-tenant presentation must not revoke tenant A's token");
        token.ReplacedByTokenId.Should().BeNull("no rotation may occur for a rejected cross-tenant refresh");
        (await db.RefreshTokens.IgnoreQueryFilters().CountAsync())
            .Should().Be(1, "no replacement token may be issued on a rejected cross-tenant refresh");
    }

    // Binds @TC-AUTH-ISO-049 (control: same-tenant refresh still rotates successfully).
    [Fact]
    public async Task Refresh_SameSubdomain_Succeeds_ISSUE049Control()
    {
        var rawToken = await SeedTenantAWithTokenAsync();

        var result = await CreateService(_tenantAId).RefreshTokenAsync(rawToken, "10.0.0.9", "Chrome", default);

        result.IsSuccess.Should().BeTrue("the owning tenant must still be able to refresh");
        result.Value!.AccessToken.Should().NotBeNullOrWhiteSpace();

        using var db = Db();
        var presented = await db.RefreshTokens.IgnoreQueryFilters().SingleAsync(t => t.Id == _tokenId);
        presented.RevokedAt.Should().NotBeNull("the presented token must be rotated (revoked) on a successful refresh");
        presented.ReplacedByTokenId.Should().NotBeNull("a successful refresh must chain to the replacement token");
        (await db.RefreshTokens.IgnoreQueryFilters().CountAsync(t => t.TenantId == _tenantAId))
            .Should().Be(2, "a fresh replacement token must be persisted for tenant A");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantAId, _dbName);

    private AuthService CreateService(Guid resolvedTenantId)
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(resolvedTenantId);
        ctx.IsResolved.Returns(true);
        ctx.IsSystemContext.Returns(false);

        return new AuthService(
            TestDbContextFactory.Create(ctx, _dbName),
            _jwtService,
            ctx,
            Substitute.For<ITotpService>(),
            _configuration,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Substitute.For<ILogger<AuthService>>(),
            Substitute.For<Hangfire.IBackgroundJobClient>());
    }

    /// <summary>
    /// Seeds a full happy-path for tenant A (active user, active tenant, role, active membership) plus one
    /// active refresh token, and returns the RAW token value to present to RefreshTokenAsync.
    /// </summary>
    private async Task<string> SeedTenantAWithTokenAsync()
    {
        var rawToken = _jwtService.GenerateRefreshToken();
        using var db = Db();

        db.Users.Add(new User
        {
            Id = _userId,
            Email = Email,
            DisplayName = "Acme User",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });

        db.Tenants.Add(new Tenant
        {
            Id = _tenantAId,
            Name = "Acme",
            Subdomain = "acme",
            Status = TenantStatus.Active,
            IdleTimeoutMinutes = 60,
            AbsoluteTimeoutHours = 24,
            CreatedAt = DateTime.UtcNow,
        });

        var role = new Role { Id = BaseEntity.NewUuidV7(), TenantId = _tenantAId, Name = "Employee", CreatedAt = DateTime.UtcNow };
        db.Roles.Add(role);

        var membership = new UserTenant
        {
            Id = BaseEntity.NewUuidV7(),
            UserId = _userId,
            TenantId = _tenantAId,
            Status = UserTenantStatus.Active,
            CreatedAt = DateTime.UtcNow,
        };
        db.UserTenants.Add(membership);
        db.UserTenantRoles.Add(new UserTenantRole
        {
            UserTenantId = membership.Id,
            RoleId = role.Id,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = "unit-test",
        });

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = _tokenId,
            UserId = _userId,
            TenantId = _tenantAId,
            TokenHash = _jwtService.HashToken(rawToken),
            IssuedAt = DateTime.UtcNow.AddMinutes(-5),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            LastActiveAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
        return rawToken;
    }
}
