// ============================================================================
// ISSUE-050 — a detected refresh-token reuse must write a queryable audit row
// (security.refresh_token_reuse_detected), not just a Serilog line.
//
// AuthService.RefreshTokenAsync, on reuse (a revoked/rotated token replayed),
// revokes the compromised lineage and then writes an AuditLog with actor = the
// token's owning user and tenant = the token's owning tenant.
//
// Harness mirrors RefreshTokenReuseLineageTests: real AuthService over InMemory
// EF; the reused token is seeded already-revoked (RevokedAt set) with a real
// HashToken hash, then replayed. We assert on the PERSISTED audit row (presence +
// EventType + actor + tenant scope), not the return value.
//
// Why it fails pre-fix: reuse detection logged to Serilog only — zero audit rows
// — so the row query returns nothing and the assertion fails. Post-fix the row
// exists.
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

public sealed class RefreshTokenReuseAuditTests
{
    private const string ReuseEvent = "security.refresh_token_reuse_detected";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _reusedId = Guid.NewGuid();
    private readonly Guid _rotatedId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;

    public RefreshTokenReuseAuditTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "hrm-api-test",
            ["Jwt:Audience"] = "hrm-client-test",
            ["Platform:BaseDomain"] = "yourhrm.test",
        }).Build();
        _jwtService = new JwtService(_configuration);
    }

    // Binds @TC-AUTH-AUDIT-050.
    [Fact]
    public async Task Refresh_ReuseDetected_WritesAudit_ISSUE050()
    {
        // Seed an already-rotated (revoked) token → replaying it is the reuse path.
        await SeedReusedTokenAsync();

        var result = await CreateService().RefreshTokenAsync("rawReused", "198.51.100.4", "Chrome", default);

        // Reuse is rejected...
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(401);

        // ...AND the security event is now in the queryable audit trail, tenant-scoped to the token's tenant
        // with the token's owning user as actor.
        using var db = Db();
        var rows = db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EventType == ReuseEvent && a.UserId == _userId && a.TenantId == _tenantId)
            .ToList();

        rows.Should().ContainSingle("a detected reuse must be recorded exactly once in the audit trail (ISSUE-050)");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private AuthService CreateService() => new(
        Db(), _jwtService, _tenantContext, Substitute.For<ITotpService>(), _configuration,
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
        Substitute.For<ILogger<AuthService>>(), Substitute.For<Hangfire.IBackgroundJobClient>());

    private async Task SeedReusedTokenAsync()
    {
        using var db = Db();
        db.Users.Add(new User { Id = _userId, Email = "u@acme.com", IsActive = true, CreatedAt = DateTime.UtcNow });

        // The reused token: already revoked (rotated → _rotatedId), so replaying it trips reuse detection.
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = _reusedId,
            UserId = _userId,
            TenantId = _tenantId,
            TokenHash = _jwtService.HashToken("rawReused"),
            IssuedAt = DateTime.UtcNow.AddMinutes(-20),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = DateTime.UtcNow.AddMinutes(-10),
            ReplacedByTokenId = _rotatedId,
        });
        // The active successor in the same lineage.
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = _rotatedId,
            UserId = _userId,
            TenantId = _tenantId,
            TokenHash = _jwtService.HashToken("rawRotated"),
            IssuedAt = DateTime.UtcNow.AddMinutes(-10),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        await db.SaveChangesAsync();
    }
}
