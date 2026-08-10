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
    private static DefaultHttpContext MakeContext(
        string method, string path, TenantStatus status = TenantStatus.Terminating)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.IsResolved.Returns(true);
        tenant.IsSystemContext.Returns(false);
        tenant.Status.Returns(status);
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

    // ── GAP-032: the TERMINAL state ─────────────────────────────────────────
    // Terminated was absent from the status switch entirely, so a tenant whose lifecycle had COMPLETED fell
    // straight through to the next middleware and kept serving requests. An access token minted before
    // termination stayed usable until it expired — precisely the window termination exists to close.
    // Unlike Terminating there is no read-only grace and no export window: the data is gone.

    [Theory]
    [InlineData("GET", "/api/v1/tenant/employees")]
    [InlineData("POST", "/api/v1/tenant/employees")]
    [InlineData("GET", "/api/v1/tenant/data-exports")]   // no export window either
    [InlineData("POST", "/api/v1/tenant/data-exports")]
    public async Task Terminated_tenant_is_refused_on_every_api_path_including_reads_and_exports_gap032(
        string method, string path)
    {
        var (nextCalled, status) = await Run(MakeContext(method, path, TenantStatus.Terminated));

        nextCalled.Should().BeFalse(
            "a terminated workspace must not reach the application at all -- a pre-termination JWT would "
            + "otherwise keep working until it expired");
        status.Should().Be(StatusCodes.Status451UnavailableForLegalReasons);
    }

    [Theory]
    [InlineData("/api/v1/auth/logout")]
    [InlineData("/api/v1/tenant/lifecycle-notice")]
    public async Task Terminated_tenant_still_reaches_auth_and_the_lifecycle_notice(string path)
    {
        // These stay open for EVERY status: the user must be able to log out, and the UI must be able to
        // read the notice that explains why the workspace is gone. Blocking them would leave the front end
        // unable to say what happened.
        var (nextCalled, _) = await Run(MakeContext("POST", path, TenantStatus.Terminated));

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Terminating_tenant_keeps_its_export_window()
    {
        // Guards the asymmetry introduced above: the export carve-out is status-dependent, and removing it
        // for Terminated must not remove it for Terminating, whose whole purpose is data extraction.
        var (nextCalled, _) = await Run(MakeContext("POST", "/api/v1/tenant/data-exports", TenantStatus.Terminating));

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Terminated_tenant_still_passes_non_api_paths_through()
    {
        // Same carve-out every other status gets: the guard is about the API surface, not static assets.
        var (nextCalled, _) = await Run(MakeContext("GET", "/health/ready", TenantStatus.Terminated));

        nextCalled.Should().BeTrue();
    }

    [Theory]
    [InlineData(TenantStatus.Active)]
    [InlineData(TenantStatus.Trial)]
    [InlineData(TenantStatus.PastDue)]
    public async Task Non_terminal_statuses_are_unaffected(TenantStatus status)
    {
        // PastDue is included deliberately: it is defined, unreachable and unenforced (Phase 1 has no
        // billing). This pins that adding Terminated did NOT accidentally start enforcing it.
        var (nextCalled, _) = await Run(MakeContext("POST", "/api/v1/tenant/employees", status));

        nextCalled.Should().BeTrue();
    }
}
