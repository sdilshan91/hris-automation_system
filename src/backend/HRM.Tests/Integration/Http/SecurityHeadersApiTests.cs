// ============================================================================
// GAP-033a — §23.4 security headers, asserted over the real HTTP pipeline.
//
// Before this, ZERO of the six headers §23.4 requires existed anywhere in the repo: not in
// src/frontend/nginx.conf (the config baked into the served image by Dockerfile:21, which set only
// Cache-Control), not in Program.cs, and there was no UseHsts/UseHttpsRedirection either. The 2026-08-08
// gap analysis rated this only 70% likely to be a real gap rather than out-of-repo infra; re-verification
// on 2026-08-21 raised that to 95% precisely BECAUSE the frontend nginx config is in this repo and is the
// natural home for them.
//
// Why a test and not just the middleware: a header block is a hand-maintained list, and this codebase's
// own systemic finding S-2 is that hand-maintained lists drift while guarded ones do not (the RLS layer
// had a coverage guard and zero holes; the EF filter layer had none and drifted to six). A header silently
// dropped during a pipeline reorder is invisible in review and invisible at runtime until someone runs a
// scanner. This pins them.
//
// Scope note: these assert the API's headers. The SPA's headers live in src/frontend/nginx.conf and are
// served by nginx, which no xUnit test exercises -- that half is verified by the go-live checklist step
// added in PR #515 (§1.7). Asserting only what this process actually serves is deliberate; a test that
// claimed to cover the SPA headers would be theatre.
// ============================================================================

using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HRM.Tests.Integration.Http;

[Collection("HttpApi")]
public sealed class SecurityHeadersApiTests
{
    private readonly ApiTestFactory _factory;

    public SecurityHeadersApiTests(ApiTestFactory factory) => _factory = factory;

    /// <summary>
    /// The four unconditional headers must be present on an ordinary response.
    /// </summary>
    [Fact]
    public async Task Api_Sends_TheFour_UnconditionalSecurityHeaders_GAP033a()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle()
            .Which.Should().Be("nosniff", "MIME sniffing turns a user-uploaded file into a script vector");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle()
            .Which.Should().Be("DENY", "the API must never be framed");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle()
            .Which.Should().Be("strict-origin-when-cross-origin");
        response.Headers.GetValues("Permissions-Policy").Should().ContainSingle()
            .Which.Should().Be("camera=(), microphone=(), geolocation=()",
                "pinning only camera=() let microphone=() and geolocation=() be dropped undetected");
    }

    /// <summary>
    /// The API returns JSON and is never framed, so its CSP is deliberately tighter than the SPA's:
    /// `default-src 'none'` is correct here and would break the SPA.
    /// </summary>
    [Fact]
    public async Task Api_Csp_Is_Locked_Down_ForAJsonApi_GAP033a()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();

        // EQUALITY, not Contain(). A substring assertion catches a DOWNGRADE of a named directive but is
        // blind to an ADDED permissive one: `default-src 'none'; script-src 'unsafe-inline' 'unsafe-eval'`
        // still contains both substrings while being materially weaker. The API's CSP is a fixed constant,
        // so equality costs nothing and closes that hole. It also pins base-uri 'none', which no substring
        // assertion covered.
        csp.Should().Be("default-src 'none'; frame-ancestors 'none'; base-uri 'none'");
    }

    /// <summary>
    /// THE ARM THAT MATTERS MOST: headers must survive an ERROR response.
    ///
    /// nginx omits add_header on 4xx/5xx unless `always` is set, and a middleware placed after the error
    /// handler would do the same. A 401/403/500 is still a response the browser acts on, so nosniff and
    /// frame-ancestors matter there as much as on a 200 — arguably more, since error bodies are where
    /// reflected content tends to appear.
    /// </summary>
    [Fact]
    public async Task SecurityHeaders_SurviveAn_ErrorResponse_GAP033a()
    {
        var client = _factory.CreateClient();

        // Unauthenticated call to a protected route -> 401/403, not 200.
        var response = await client.GetAsync("/api/v1/tenant/employees");

        // 401/403 only. Accepting BadRequest too would let a genuine regression -- the route starting to
        // 400 on a malformed tenant instead of rejecting the missing credential -- slip through unnoticed.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);

        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle()
            .Which.Should().Be("nosniff",
                "the headers are set before the pipeline can short-circuit, so an error response keeps them");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle().Which.Should().Be("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle()
            .Which.Should().Be("strict-origin-when-cross-origin");
        response.Headers.GetValues("Content-Security-Policy").Single()
            .Should().Be("default-src 'none'; frame-ancestors 'none'; base-uri 'none'",
                "the CSP must survive the error path too, not just the 200 path");
    }

    /// <summary>
    /// HSTS is emitted only over HTTPS. Sending it over plain HTTP is ignored by browsers (RFC 6797 §7.2)
    /// and would be misleading in local dev, so its absence here is the CORRECT behaviour — asserted so a
    /// future "just always send it" change has to justify itself.
    /// </summary>
    [Fact]
    public async Task Hsts_IsNotSent_OverPlainHttp_RFC6797()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.Headers.Contains("Strict-Transport-Security").Should().BeFalse(
            "RFC 6797 §7.2: a UA must ignore HSTS received over non-secure transport, so emitting it "
            + "over HTTP is noise that hides whether the real HTTPS path sets it");
    }

    /// <summary>
    /// THE POSITIVE COUNTERPART to <see cref="Hsts_IsNotSent_OverPlainHttp_RFC6797"/>, and the arm this
    /// suite originally lacked.
    ///
    /// An absence assertion alone is one-sided: it stays green when the feature is DELETED. With only the
    /// over-HTTP arm, removing the HSTS line from Program.cs entirely -- or inverting its condition to
    /// `if (!ctx.Request.IsHttps)` -- left all four original tests green, so the production behaviour
    /// (HSTS actually reaching a browser over TLS) had zero test power behind it.
    ///
    /// TestServer sets Request.IsHttps from the request URI scheme, so an https BaseAddress exercises the
    /// real `if (ctx.Request.IsHttps)` branch rather than simulating it.
    /// </summary>
    [Fact]
    public async Task Hsts_IsSent_OverHttps_GAP033a()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.GetAsync("/health");

        response.Headers.GetValues("Strict-Transport-Security").Should().ContainSingle()
            .Which.Should().Be("max-age=31536000; includeSubDomains",
                "a one-year max-age with includeSubDomains is what §23.4 requires; a shortened max-age is "
                + "a silent downgrade that no absence assertion can catch");
    }
}
