// ============================================================================
// BUG-003 regression: the tenant-access guard must reject an authenticated
// request whose JWT tenant differs from the tenant resolved from the (spoofable)
// host/subdomain — while leaving every legitimate flow untouched.
// ============================================================================

using FluentAssertions;
using HRM.Api.Middleware;
using HRM.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class TenantAccessGuardMiddlewareTests
{
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private async Task<(int status, bool nextCalled)> RunAsync(
        ICurrentUser currentUser, ITenantContext tenantContext)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var middleware = new TenantAccessGuardMiddleware(next, NullLogger<TenantAccessGuardMiddleware>.Instance);
        await middleware.InvokeAsync(ctx, currentUser, tenantContext);

        return (ctx.Response.StatusCode, nextCalled);
    }

    private ICurrentUser User(bool authenticated, Guid tenantId)
    {
        var u = Substitute.For<ICurrentUser>();
        u.IsAuthenticated.Returns(authenticated);
        u.TenantId.Returns(tenantId);
        return u;
    }

    private ITenantContext Tenant(bool resolved, bool system, Guid tenantId)
    {
        var t = Substitute.For<ITenantContext>();
        t.IsResolved.Returns(resolved);
        t.IsSystemContext.Returns(system);
        t.TenantId.Returns(tenantId);
        return t;
    }

    [Fact]
    public async Task MismatchedTenant_IsRejected403_AndDoesNotCallNext()
    {
        var (status, nextCalled) = await RunAsync(
            User(authenticated: true, tenantId: _tenantA),
            Tenant(resolved: true, system: false, tenantId: _tenantB));

        status.Should().Be(StatusCodes.Status403Forbidden);
        nextCalled.Should().BeFalse("the request must be short-circuited before any tenant-scoped work");
    }

    [Fact]
    public async Task MatchingTenant_IsAllowed()
    {
        var (status, nextCalled) = await RunAsync(
            User(authenticated: true, tenantId: _tenantA),
            Tenant(resolved: true, system: false, tenantId: _tenantA));

        nextCalled.Should().BeTrue();
        status.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Unauthenticated_IsAllowed()
    {
        var (_, nextCalled) = await RunAsync(
            User(authenticated: false, tenantId: Guid.Empty),
            Tenant(resolved: true, system: false, tenantId: _tenantB));

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task SystemContext_IsAllowed()
    {
        var (_, nextCalled) = await RunAsync(
            User(authenticated: true, tenantId: _tenantA),
            Tenant(resolved: true, system: true, tenantId: Guid.Empty));

        nextCalled.Should().BeTrue("system/admin context is exempt");
    }

    [Fact]
    public async Task NoTenantResolved_IsAllowed()
    {
        var (_, nextCalled) = await RunAsync(
            User(authenticated: true, tenantId: _tenantA),
            Tenant(resolved: false, system: false, tenantId: Guid.Empty));

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task TokenWithoutBusinessTenant_IsAllowed()
    {
        // Platform-admin style token (no business tenant claim) must not be blocked.
        var (_, nextCalled) = await RunAsync(
            User(authenticated: true, tenantId: Guid.Empty),
            Tenant(resolved: true, system: false, tenantId: _tenantB));

        nextCalled.Should().BeTrue();
    }
}
