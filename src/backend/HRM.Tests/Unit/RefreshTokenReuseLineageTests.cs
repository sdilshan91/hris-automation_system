// ============================================================================
// BUG-043 regression: on refresh-token reuse, only the COMPROMISED token's
// lineage (rotation chain) is revoked — not every session for the user+tenant.
// An independent concurrent session (a separate chain) must survive.
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

public sealed class RefreshTokenReuseLineageTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;

    // Chain A (compromised): A1 rotated → A2 (active). Chain B (independent): B1 rotated → B2 (active).
    private readonly Guid _a1 = Guid.NewGuid();
    private readonly Guid _a2 = Guid.NewGuid();
    private readonly Guid _b1 = Guid.NewGuid();
    private readonly Guid _b2 = Guid.NewGuid();

    public RefreshTokenReuseLineageTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "hrm-api-test", ["Jwt:Audience"] = "hrm-client-test", ["Platform:BaseDomain"] = "yourhrm.test",
        }).Build();
        _jwtService = new JwtService(_configuration);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private RefreshToken Token(Guid id, string raw, bool revoked, Guid? replacedBy) => new()
    {
        Id = id,
        UserId = _userId,
        TenantId = _tenantId,
        TokenHash = _jwtService.HashToken(raw),
        IssuedAt = DateTime.UtcNow.AddMinutes(-10),
        ExpiresAt = DateTime.UtcNow.AddDays(7),
        RevokedAt = revoked ? DateTime.UtcNow.AddMinutes(-5) : null,
        ReplacedByTokenId = replacedBy,
    };

    private async Task SeedAsync()
    {
        using var db = Db();
        db.Users.Add(new User { Id = _userId, Email = "u@acme.com", IsActive = true });
        db.RefreshTokens.AddRange(
            Token(_a1, "rawA1", revoked: true, replacedBy: _a2),   // reused (old) token
            Token(_a2, "rawA2", revoked: false, replacedBy: null), // active in the compromised chain
            Token(_b1, "rawB1", revoked: true, replacedBy: _b2),
            Token(_b2, "rawB2", revoked: false, replacedBy: null)); // active INDEPENDENT session
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Reuse_RevokesOnlyTheCompromisedChain_NotIndependentSessions()
    {
        await SeedAsync();

        // Present the already-rotated A1 → reuse detected.
        var result = await CreateService().RefreshTokenAsync("rawA1", null, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(401);

        using var db = Db();
        var tokens = await db.RefreshTokens.IgnoreQueryFilters().ToDictionaryAsync(t => t.Id);
        tokens[_a2].RevokedAt.Should().NotBeNull("the compromised chain's active token must be revoked");
        tokens[_b2].RevokedAt.Should().BeNull("an independent session must survive one chain's reuse");
        tokens[_b1].RevokedAt.Should().NotBeNull("(B1 was already rotated/revoked; unchanged)");
    }

    private AuthService CreateService() => new(
        Db(), _jwtService, _tenantContext, Substitute.For<ITotpService>(), _configuration,
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
        Substitute.For<ILogger<AuthService>>(), Substitute.For<Hangfire.IBackgroundJobClient>());
}
