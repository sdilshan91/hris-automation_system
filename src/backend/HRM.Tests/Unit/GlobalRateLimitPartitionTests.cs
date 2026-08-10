// ============================================================================
// GAP-018 — the global rate limiter's partition key.
//
// Before this, all three named policies partitioned on CLIENT IP and were attached to ANONYMOUS endpoints,
// so every authenticated tenant endpoint was unthrottled. These arms exist because the HTTP integration host
// sets RateLimiting:Disabled=true (it drives ~12 test classes from one identity), which means the limiter is
// structurally unreachable through that harness — a control the suite cannot exercise is a control on paper.
// ============================================================================

using FluentAssertions;
using HRM.Api.RateLimiting;
using HRM.Application.Common.Interfaces;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class GlobalRateLimitPartitionTests
{
    private static ITenantContext Tenant(Guid tenantId, bool system = false)
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.IsSystemContext.Returns(system);
        ctx.IsResolved.Returns(true);
        return ctx;
    }

    private static ICurrentUser User(Guid userId, bool authenticated = true)
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(authenticated);
        user.UserId.Returns(userId);
        return user;
    }

    private static string Resolve(
        ITenantContext? tenant = null, ICurrentUser? user = null,
        string path = "/api/v1/tenant/employees", bool disabled = false, string ip = "203.0.113.7")
        => GlobalRateLimitPartition.Resolve(path, disabled, tenant, user, ip);

    // ── the point of the whole change ───────────────────────────────────────

    [Fact]
    public void An_authenticated_request_is_partitioned_by_tenant_AND_user_not_by_ip()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var key = Resolve(Tenant(tenantId), User(userId));

        key.Should().Be($"t:{tenantId}:u:{userId}");
        key.Should().NotContain("203.0.113.7",
            "IP is the wrong identity for authenticated traffic — an office behind one NAT shares an address, "
            + "and a single abusive token roams across addresses");
    }

    [Fact]
    public void Two_users_in_the_SAME_tenant_get_separate_allowances()
    {
        var tenantId = Guid.NewGuid();

        Resolve(Tenant(tenantId), User(Guid.NewGuid()))
            .Should().NotBe(Resolve(Tenant(tenantId), User(Guid.NewGuid())));
    }

    [Fact]
    public void One_noisy_tenant_cannot_consume_another_tenants_allowance()
    {
        // Same user id deliberately: proves the TENANT is part of the key, not just the user.
        var userId = Guid.NewGuid();

        Resolve(Tenant(Guid.NewGuid()), User(userId))
            .Should().NotBe(Resolve(Tenant(Guid.NewGuid()), User(userId)));
    }

    [Fact]
    public void The_same_user_on_a_different_ip_shares_ONE_allowance()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var fromOffice = Resolve(Tenant(tenantId), User(userId), ip: "203.0.113.7");
        var fromHome = Resolve(Tenant(tenantId), User(userId), ip: "198.51.100.42");

        fromOffice.Should().Be(fromHome, "a roaming token must not escape its bucket by changing address");
    }

    // ── anonymous fallback ──────────────────────────────────────────────────

    [Fact]
    public void Anonymous_traffic_falls_back_to_the_client_ip()
    {
        Resolve(user: User(Guid.NewGuid(), authenticated: false), ip: "198.51.100.9")
            .Should().Be("ip:198.51.100.9");
    }

    [Fact]
    public void A_missing_user_service_is_treated_as_anonymous_not_as_exempt()
    {
        // Fail-closed direction: no identity means the IP bucket, never "unlimited".
        var key = Resolve(Tenant(Guid.NewGuid()), user: null, ip: "198.51.100.9");

        key.Should().Be("ip:198.51.100.9");
        key.Should().NotBe(GlobalRateLimitPartition.Unlimited);
    }

    // ── the three exemptions, each with a reason ────────────────────────────

    [Fact]
    public void The_RateLimiting_Disabled_switch_exempts_everything()
    {
        // The HTTP integration host sets this; the same flag already exempts auth-login.
        Resolve(Tenant(Guid.NewGuid()), User(Guid.NewGuid()), disabled: true)
            .Should().Be(GlobalRateLimitPartition.Unlimited);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/swagger/index.html")]
    public void Infra_probes_and_dev_docs_are_exempt(string path)
    {
        // /health deliberately bypasses tenant resolution too, and throttling liveness checks would take the
        // service out of its own load balancer.
        Resolve(path: path).Should().Be(GlobalRateLimitPartition.Unlimited);
    }

    [Fact]
    public void System_context_is_exempt_so_cross_tenant_operator_sweeps_are_not_throttled()
    {
        Resolve(Tenant(Guid.NewGuid(), system: true), User(Guid.NewGuid()))
            .Should().Be(GlobalRateLimitPartition.SystemContext);
    }

    [Fact]
    public void A_normal_tenant_request_is_NOT_treated_as_system_context()
    {
        // Guards the exemption in the direction that matters: it must not leak to ordinary tenants.
        Resolve(Tenant(Guid.NewGuid(), system: false), User(Guid.NewGuid()))
            .Should().NotBe(GlobalRateLimitPartition.SystemContext);
    }

    [Fact]
    public void An_api_path_that_merely_CONTAINS_health_is_not_exempt()
    {
        // StartsWith, not Contains — otherwise a tenant endpoint could dodge the limiter by having "health"
        // somewhere in its route (e.g. a future /api/v1/tenant/health-benefits).
        Resolve(Tenant(Guid.NewGuid()), User(Guid.NewGuid()), path: "/api/v1/tenant/health-benefits")
            .Should().NotBe(GlobalRateLimitPartition.Unlimited);
    }
}
