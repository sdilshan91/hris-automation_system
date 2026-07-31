// ============================================================================
// D3 (ISSUE-358): ScimEntitlementMiddleware — the per-request SCIM feature gate,
// pre-registered ahead of any SCIM controller. A /scim/v2 request is denied 403
// feature_not_entitled when the tenant's plan lacks the Scim flag, allowed when
// it has it, and every non-/scim/v2 path falls open. Fail-open: a null (unread-
// able) flag set denies nothing. These arms boot no stack — they pin the gate
// and its fail-open direction deterministically.
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using HRM.Api.Middleware;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class ScimEntitlementMiddlewareTests
{
    private static DefaultHttpContext MakeContext(
        string path,
        IReadOnlyCollection<string>? featureFlags,
        bool isResolved = true,
        bool isSystemContext = false)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.IsResolved.Returns(isResolved);
        tenant.IsSystemContext.Returns(isSystemContext);
        tenant.TenantId.Returns(Guid.NewGuid());
        tenant.FeatureFlags.Returns(featureFlags);

        var services = new ServiceCollection();
        services.AddSingleton(tenant);

        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Request.Method = "GET";
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static async Task<(bool nextCalled, int status, string? code)> Run(HttpContext ctx)
    {
        var nextCalled = false;
        var mw = new ScimEntitlementMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            Substitute.For<ILogger<ScimEntitlementMiddleware>>());

        await mw.InvokeAsync(ctx);

        string? code = null;
        if (ctx.Response.Body is MemoryStream ms && ms.Length > 0)
        {
            ms.Position = 0;
            using var doc = JsonDocument.Parse(ms.ToArray());
            if (doc.RootElement.TryGetProperty("code", out var c))
                code = c.GetString();
        }

        return (nextCalled, ctx.Response.StatusCode, code);
    }

    // Authoritative-empty set (resolved plan that grants no flags) — the "Scim not entitled" fixture.
    private static readonly IReadOnlyCollection<string> NoFlags = PlanFeatureFlagKeys.Derive(new())!;
    private static readonly IReadOnlyCollection<string> ScimFlag =
        PlanFeatureFlagKeys.Derive(new() { Scim = true })!;

    [Fact]
    [Trait("TC", "TC-ADM-358")]
    public async Task Scim_path_denied_without_flag()
    {
        var (nextCalled, status, code) = await Run(MakeContext("/scim/v2/Users", NoFlags));

        nextCalled.Should().BeFalse();
        status.Should().Be(StatusCodes.Status403Forbidden);
        code.Should().Be("feature_not_entitled");
    }

    [Fact]
    [Trait("TC", "TC-ADM-358")]
    public async Task Scim_path_allowed_with_flag()
    {
        // SAME path, only the flag differs (Scim now present) ⇒ passes. Single-variable discriminator.
        var (nextCalled, status, code) = await Run(MakeContext("/scim/v2/Users", ScimFlag));

        nextCalled.Should().BeTrue();
        status.Should().Be(StatusCodes.Status200OK);
        code.Should().BeNull();
    }

    [Theory]
    [Trait("TC", "TC-ADM-358")]
    [InlineData("/api/v1/tenant/employees")]
    [InlineData("/scim")]        // NOT under /scim/v2 — unmapped, must fall open even with no flag
    [InlineData("/scim/v1/Users")]
    [InlineData("/scim/v2x/Users")] // sibling-prefix trap: segment match must NOT treat this as /scim/v2
    public async Task Unmapped_path_unaffected_even_without_flag(string path)
    {
        // A path outside the gated /scim/v2 prefix must pass regardless of entitlement (positive-list).
        var (nextCalled, status, code) = await Run(MakeContext(path, NoFlags));

        nextCalled.Should().BeTrue();
        status.Should().Be(StatusCodes.Status200OK);
        code.Should().BeNull();
    }

    [Fact]
    [Trait("TC", "TC-ADM-358")]
    public async Task FailOpen_null_flags_never_denies_scim()
    {
        // No plan resolved / flags unreadable ⇒ null set ⇒ fail-open ⇒ SCIM passes. This is the arm the mutation
        // guide says must DIE if fail-open is inverted to fail-closed.
        var (nextCalled, status, code) = await Run(MakeContext("/scim/v2/Users", featureFlags: null));

        nextCalled.Should().BeTrue();
        status.Should().Be(StatusCodes.Status200OK);
        code.Should().BeNull();
    }

    [Fact]
    [Trait("TC", "TC-ADM-358")]
    public async Task SystemContext_bypasses_entirely()
    {
        var (nextCalled, _, code) = await Run(
            MakeContext("/scim/v2/Users", NoFlags, isSystemContext: true));

        nextCalled.Should().BeTrue();
        code.Should().BeNull();
    }

    [Fact]
    [Trait("TC", "TC-ADM-358")]
    public async Task UnresolvedTenant_bypasses_entirely()
    {
        var (nextCalled, _, code) = await Run(
            MakeContext("/scim/v2/Users", NoFlags, isResolved: false));

        nextCalled.Should().BeTrue();
        code.Should().BeNull();
    }
}
