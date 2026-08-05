// ============================================================================
// Wave 5 / RLS flip prep — /api/v1/system/* must be admin-context only.
//
// Nothing bound the platform-admin namespace to the admin host. A platform-admin JWT could reach
// https://acme.yourhrm.com/api/v1/system/tenants, and the request would resolve tenant `acme`. That works
// today only because those services use IgnoreQueryFilters() and the EF filter is fail-open — under RLS the
// same request routes to the tenant role, where the cross-tenant admin queries return zero rows and the
// NULL-tenant audit insert violates the strict WITH CHECK.
//
// The guard reads the RESOLVED context rather than the Host header, because TenantResolutionMiddleware also
// accepts the dev X-Tenant-Subdomain header — a raw Host check would pass in production and reject every
// admin request on a developer's machine.
// ============================================================================

using System.Text;
using System.Text.Json;
using FluentAssertions;
using HRM.Api.Middleware;
using HRM.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class SystemEndpointHostGuardTests
{
    private static ITenantContext Context(
        bool isSystemContext, string subdomain = "acme", Guid? tenantId = null)
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.IsSystemContext.Returns(isSystemContext);
        ctx.IsResolved.Returns(true);
        ctx.TenantId.Returns(isSystemContext ? Guid.Empty : tenantId ?? Guid.NewGuid());
        ctx.Subdomain.Returns(isSystemContext ? "admin" : subdomain);
        return ctx;
    }

    /// <summary>The platform tenant — where real platform administrators actually live.</summary>
    private static ITenantContext PlatformTenantContext() => Context(false, "platform");

    private static async Task<(int StatusCode, bool NextCalled, string Body)> InvokeAsync(
        string path, ITenantContext tenantContext)
    {
        var nextCalled = false;
        var middleware = new SystemEndpointHostGuardMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<SystemEndpointHostGuardMiddleware>.Instance,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());

        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, tenantContext);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        return (context.Response.StatusCode, nextCalled, body);
    }

    [Fact]
    public async Task A_system_endpoint_on_a_TENANT_host_is_refused_WaveFivePrep()
    {
        var (status, nextCalled, body) = await InvokeAsync("/api/v1/system/tenants", Context(false));

        status.Should().Be(StatusCodes.Status403Forbidden);
        nextCalled.Should().BeFalse("the request must not reach the controller at all");

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("code").GetString()
            .Should().Be("system_endpoint_requires_admin_host",
                "the code must say WHY, so an operator does not chase a permissions problem");
    }

    [Fact]
    public async Task A_system_endpoint_on_the_ADMIN_host_passes_through_WaveFivePrep()
    {
        var (_, nextCalled, _) = await InvokeAsync("/api/v1/system/tenants", Context(true));

        nextCalled.Should().BeTrue("the platform console must keep working on the admin host");
    }

    [Fact]
    public async Task The_PLATFORM_tenant_is_elevated_not_refused_WaveFivePrep()
    {
        // The correction that mattered. Platform administrators are users of the `platform` TENANT, not of
        // admin.* — admin.* resolves to TenantId = Guid.Empty, which no user_tenants row can match, so nobody
        // can log in there at all. A guard that demanded IsSystemContext would have 403'd the entire platform
        // console in production. Four existing HTTP tests caught this; without them it would have shipped.
        var platform = PlatformTenantContext();

        var (_, nextCalled, _) = await InvokeAsync("/api/v1/system/tenants", platform);

        nextCalled.Should().BeTrue("the real platform console must keep working");
        platform.Received(1).SetSystemContext();
    }

    [Fact]
    public async Task Elevation_routes_the_request_to_the_privileged_connection_WaveFivePrep()
    {
        // SetSystemContext is not cosmetic: it is what makes ConnectionRoutingInterceptor pick the BYPASSRLS
        // role. Without it the admin queries run on the tenant role and silently return zero rows under RLS.
        var platform = PlatformTenantContext();

        await InvokeAsync("/api/v1/system/monitoring/health", platform);

        platform.Received(1).SetSystemContext();
    }

    [Fact]
    public async Task An_ORDINARY_tenant_is_never_elevated_WaveFivePrep()
    {
        // The security half: acme.* must not reach the platform namespace, and must certainly never be handed
        // a privileged connection.
        var acme = Context(false);

        var (status, nextCalled, _) = await InvokeAsync("/api/v1/system/tenants", acme);

        status.Should().Be(StatusCodes.Status403Forbidden);
        nextCalled.Should().BeFalse();
        acme.DidNotReceive().SetSystemContext();
    }

    [Fact]
    public async Task Ordinary_tenant_traffic_is_untouched_WaveFivePrep()
    {
        // The guard sits in the hot path for EVERY request. If the prefix match were wrong, it would 403 the
        // entire application rather than one namespace.
        var (_, nextCalled, _) = await InvokeAsync("/api/v1/employees", Context(false));

        nextCalled.Should().BeTrue();
    }

    [Theory]
    [InlineData("/API/V1/SYSTEM/tenants")]           // routing is case-insensitive; the guard must be too
    [InlineData("/api/v1/system")]                    // the namespace root itself
    [InlineData("/api/v1/system/monitoring/health")]  // nested
    public async Task Every_form_of_the_system_path_is_covered_WaveFivePrep(string path)
    {
        var (status, nextCalled, _) = await InvokeAsync(path, Context(false));

        status.Should().Be(StatusCodes.Status403Forbidden);
        nextCalled.Should().BeFalse();
    }

    [Theory]
    [InlineData("/api/v1/systemic-risk")]   // shares a PREFIX but is a different segment
    [InlineData("/api/v1/systems")]
    public async Task A_path_that_merely_STARTS_with_system_is_not_blocked_WaveFivePrep(string path)
    {
        // Segment-boundary matching, not string prefix. A naive StartsWith would silently 403 any future
        // endpoint whose name happens to begin with "system".
        var (_, nextCalled, _) = await InvokeAsync(path, Context(false));

        nextCalled.Should().BeTrue("only the /api/v1/system SEGMENT is the platform namespace");
    }
}
