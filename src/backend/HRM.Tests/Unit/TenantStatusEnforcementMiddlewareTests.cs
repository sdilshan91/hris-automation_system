// ============================================================================
// US-ADM-004 (BR-6): TenantStatusEnforcementMiddleware — terminating-tenant
// read-only grace. GET/HEAD/OPTIONS and EXPORT endpoints must stay reachable so
// the tenant can extract its data during the grace window; every other write is
// 403. ISSUE-217 regression: the GDPR data-export path was wrongly blocked.
// ============================================================================

using FluentAssertions;
using HRM.Api.Middleware;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class TenantStatusEnforcementMiddlewareTests
{
    private static DefaultHttpContext MakeContext(string method, string path)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.IsResolved.Returns(true);
        tenant.IsSystemContext.Returns(false);
        tenant.Status.Returns(TenantStatus.Terminating);
        tenant.TenantId.Returns(Guid.NewGuid());

        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);

        var services = new ServiceCollection();
        services.AddSingleton(tenant);
        services.AddSingleton(user);

        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        return ctx;
    }

    private static async Task<(bool nextCalled, int status)> Run(HttpContext ctx)
    {
        var nextCalled = false;
        var mw = new TenantStatusEnforcementMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            Substitute.For<ILogger<TenantStatusEnforcementMiddleware>>());
        await mw.InvokeAsync(ctx);
        return (nextCalled, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Terminating_tenant_allows_GDPR_data_export_POST_issue217()
    {
        // ISSUE-217: `/api/v1/tenant/data-exports` has a hyphen (not a slash) before "exports", so the
        // `/export(s)` substring markers missed it and the primary data-export was 403'd during the exact
        // grace window whose purpose is data extraction.
        var (nextCalled, status) = await Run(MakeContext("POST", "/api/v1/tenant/data-exports"));

        nextCalled.Should().BeTrue();
        status.Should().Be(StatusCodes.Status200OK); // default — NOT rewritten to 403
    }

    [Fact]
    public async Task Terminating_tenant_allows_audit_log_export_POST()
    {
        // The already-working sibling — proves the export allowance itself is intact.
        var (nextCalled, _) = await Run(MakeContext("POST", "/api/v1/tenant/audit-logs/export"));

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Terminating_tenant_blocks_a_non_export_write_403()
    {
        // The read-only grace is still enforced for ordinary writes — proves the fix didn't open the gate.
        var (nextCalled, status) = await Run(MakeContext("POST", "/api/v1/tenant/employees"));

        nextCalled.Should().BeFalse();
        status.Should().Be(StatusCodes.Status403Forbidden);
    }
}
