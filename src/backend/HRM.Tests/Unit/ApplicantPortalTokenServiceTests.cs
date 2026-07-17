// ============================================================================
// US-REC-008 NFR-6: ApplicantPortalTokenService rate limiting.
// ISSUE-130: the per-email guard misses enumeration via rotating emails; a
// per-IP throttle caps how many tokens one IP may issue (across all emails).
// Uses EF Core InMemory through the real service.
// ============================================================================

using System.Net;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class ApplicantPortalTokenServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _config;

    public ApplicantPortalTokenServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Recruitment:PortalTokenSecret"] = "unit-test-portal-secret-0123456789abcdef",
            })
            .Build();
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private ApplicantPortalTokenService Service(string? ip)
    {
        IHttpContextAccessor? accessor = null;
        if (ip is not null)
        {
            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
            accessor = Substitute.For<IHttpContextAccessor>();
            accessor.HttpContext.Returns(ctx);
        }
        return new ApplicantPortalTokenService(
            Db(), _tenantContext, _config, NullLogger<ApplicantPortalTokenService>.Instance, accessor);
    }

    [Fact]
    public async Task Issue_throttles_by_IP_across_rotating_emails_issue130()
    {
        const string ip = "203.0.113.7";

        // 10 issues with DISTINCT emails from one IP all succeed (the per-email guard never fires).
        for (int i = 0; i < 10; i++)
            (await Service(ip).IssueAsync($"cand{i}@x.test")).IsSuccess.Should().BeTrue();

        // The 11th from the SAME IP (a fresh email) is throttled by the per-IP cap.
        var blocked = await Service(ip).IssueAsync("cand-extra@x.test");

        blocked.IsFailure.Should().BeTrue();
        blocked.StatusCode.Should().Be(429);
        blocked.ErrorCode.Should().Be("rate_limited");
    }

    [Fact]
    public async Task Issue_from_a_different_IP_is_not_throttled_issue130()
    {
        for (int i = 0; i < 10; i++)
            await Service("203.0.113.7").IssueAsync($"cand{i}@x.test");

        // A different IP is unaffected by the first IP's count.
        var other = await Service("198.51.100.9").IssueAsync("cand-extra@x.test");

        other.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Issue_without_httpcontext_skips_the_ip_throttle_issue130()
    {
        // No HttpContext (e.g. a background path) → the per-IP throttle is skipped; distinct-email issues all pass.
        for (int i = 0; i < 11; i++)
            (await Service(null).IssueAsync($"cand{i}@x.test")).IsSuccess.Should().BeTrue();
    }
}
