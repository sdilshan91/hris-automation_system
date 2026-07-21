// ============================================================================
// DF-7 (US-REC-008 NFR-6, ISSUE-130): DbCountPortalLinkIpRateLimiter — the
// single-instance DB sliding-window fallback used when Redis is not configured.
// Proves the MOVED logic is behaviour-identical to the original inline guard:
// 10/hr/(tenant,IP) enforced, a different IP is independent, and tokens older
// than the window are not counted. EF Core InMemory through the real limiter.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Options;
using Xunit;

namespace HRM.Tests.Unit;

public sealed class DbCountPortalLinkIpRateLimiterTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantId, _dbName);

    private static IPortalLinkIpRateLimiter Limiter(AppDbContext db, int max = 10, int window = 3600, TimeProvider? clock = null)
        => new DbCountPortalLinkIpRateLimiter(
            db, Options.Create(new PortalLinkRateLimitOptions { MaxIssuesPerIp = max, IpWindowSeconds = window }), clock);

    /// <summary>A fixed clock so the sliding-window edge can be asserted deterministically.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task SeedTokensAsync(string ip, int count, DateTime? createdAt = null)
    {
        using var db = Db();
        var now = createdAt ?? DateTime.UtcNow;
        for (int i = 0; i < count; i++)
        {
            db.ApplicantPortalTokens.Add(new ApplicantPortalToken
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                ApplicantEmail = $"cand{i}@x.test",
                TokenHash = Guid.NewGuid().ToString("N"),
                ExpiresAt = now.AddDays(30),
                CreatedAt = now,
                RequestIp = ip,
                IsDeleted = false,
            });
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    [Trait("TC", "TC-REC-011")]
    public async Task UnderCap_Allows_AtCap_Blocks_PerTenantIp()
    {
        const string ip = "203.0.113.7";
        await SeedTokensAsync(ip, 9);

        // 9 already issued this hour → the 10th is still allowed.
        using (var db = Db())
            (await Limiter(db).TryAcquireAsync(_tenantId, ip)).Should().BeTrue("9 < cap of 10");

        // Push to 10 issued → now at the cap → blocked.
        await SeedTokensAsync(ip, 1);
        using (var db = Db())
            (await Limiter(db).TryAcquireAsync(_tenantId, ip)).Should().BeFalse("10 >= cap of 10");
    }

    [Fact]
    [Trait("TC", "TC-REC-011")]
    public async Task DifferentIp_IsIndependent()
    {
        await SeedTokensAsync("203.0.113.7", 10); // exhaust the cap for the first IP

        using var db = Db();
        (await Limiter(db).TryAcquireAsync(_tenantId, "198.51.100.9"))
            .Should().BeTrue("a different IP has its own (tenant, IP) count and is unaffected");
    }

    [Fact]
    [Trait("TC", "TC-REC-011")]
    public async Task TokensOlderThanWindow_AreNotCounted()
    {
        const string ip = "203.0.113.7";
        // 10 tokens but all issued two hours ago — outside the 1-hour sliding window, so they must not count.
        await SeedTokensAsync(ip, 10, createdAt: DateTime.UtcNow.AddHours(-2));

        using var db = Db();
        (await Limiter(db).TryAcquireAsync(_tenantId, ip))
            .Should().BeTrue("tokens older than the window fall out of the sliding-window count");
    }

    [Fact]
    [Trait("TC", "TC-REC-011")]
    public async Task WindowEdge_OneSecondInsideCounts_OneSecondOutsideDoesNot()
    {
        // Pin the clock so the boundary is exact. Predicate is CreatedAt > now - window (strictly greater).
        var now = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedClock(now);
        var edge = now.UtcDateTime.AddSeconds(-3600); // exactly one window ago

        const string insideIp = "203.0.113.7";
        const string outsideIp = "198.51.100.9";
        // 10 tokens 1s INSIDE the window (created just after the cutoff) → counted → at cap → block.
        await SeedTokensAsync(insideIp, 10, createdAt: edge.AddSeconds(1));
        // 10 tokens 1s OUTSIDE the window (created just before the cutoff) → not counted → allow.
        await SeedTokensAsync(outsideIp, 10, createdAt: edge.AddSeconds(-1));

        using (var db = Db())
            (await Limiter(db, clock: clock).TryAcquireAsync(_tenantId, insideIp))
                .Should().BeFalse("rows 1s inside the sliding window are counted → at the cap");
        using (var db = Db())
            (await Limiter(db, clock: clock).TryAcquireAsync(_tenantId, outsideIp))
                .Should().BeTrue("rows 1s outside the sliding window fall out of the count");
    }
}
