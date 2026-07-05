using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

// Regression for the rate-limiting cluster: ISSUE-052 (US-AUTH-004, TC-AUTH-010 step 10) and
// ISSUE-102 (US-REC-002, TC-REC-002-11 step 2).

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Regression tests for the rate-limiting cluster: ISSUE-052 (POST /api/v1/auth/forgot-password) and
/// ISSUE-102 (the anonymous public application-submit endpoint). These are <b>live behavioral 429
/// tests</b>, not wiring guards: they boot the genuine ASP.NET Core pipeline via <see cref="ApiTestFactory"/>
/// (real routing → <c>UseRateLimiter</c> → controller) and fire N+1 requests at each protected endpoint,
/// asserting the first N are NOT throttled and the (N+1)th returns <see cref="HttpStatusCode.TooManyRequests"/>.
///
/// <para><b>Why this fails pre-fix / passes post-fix.</b> The fix (a) registers the named limiter policies
/// <c>"forgot-password"</c> (5/hour/IP) and <c>"public-application"</c> (10/hour/IP) via
/// <c>AddRateLimiter</c> in <c>Program.cs</c> and (b) applies <c>[EnableRateLimiting("...")]</c> to
/// <c>AuthController.ForgotPassword</c> and <c>CareersController.Apply</c>. With no policy registered and no
/// attribute on the action, every request passes through untouched, so the (N+1)th returns its normal status
/// (200 / 400) and the 429 assertion fails. Once wired, the (N+1)th is rejected at the limiter middleware
/// before the action runs.</para>
///
/// <para><b>Partitioning under TestServer.</b> The limiter partitions by client IP via
/// <c>Connection.RemoteIpAddress?.ToString() ?? "unknown"</c>. TestServer supplies no remote IP, so every
/// request from this test shares the single <c>"unknown"</c> partition — exactly the "same client" the guard
/// needs. The limiter is a <b>fixed-window</b> in-memory counter, so the requests below trip the limit within
/// the window without any real-time wait.</para>
///
/// <para><b>Permits are consumed before the action.</b> The rate-limiter middleware acquires a lease as soon
/// as the endpoint is selected, before model binding / the action body. So a malformed request that would
/// otherwise 400 (e.g. an empty multipart apply with no résumé file) still consumes a permit — which is why
/// the assertions only distinguish "429 vs. not-429" and do not require the first N requests to succeed.</para>
///
/// <para><b>Shared-counter coupling (important).</b> This class joins the shared <c>"HttpApi"</c> collection,
/// so the in-memory limiter state lives in the one app instance shared across all HTTP integration classes.
/// No other test in the suite currently touches <c>/auth/forgot-password</c> or <c>/careers/.../apply</c>, so
/// these fixed-window counters are effectively private to this class. If a future test starts hitting either
/// endpoint from the same shared app, it would share these counters and could contaminate these assertions —
/// move this class to its own <c>IClassFixture&lt;ApiTestFactory&gt;</c> (a dedicated app + container) at that
/// point to restore isolation.</para>
/// </summary>
[Collection("HttpApi")]
public sealed class RateLimitClusterApiTests
{
    // Mirrors Program.cs: "forgot-password" PermitLimit = 5, "public-application" PermitLimit = 10.
    private const int ForgotPasswordLimit = 5;
    private const int PublicApplicationLimit = 10;

    private readonly ApiTestFactory _factory;

    public RateLimitClusterApiTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// ISSUE-052 / US-AUTH-004 FR-9: the forgot-password endpoint must throttle repeated requests from the
    /// same client. The first 5 requests are permitted (200, per enumeration-protection design); the 6th is
    /// rejected with 429. Fails pre-fix because, without the <c>"forgot-password"</c> policy + attribute, the
    /// 6th request also returns 200.
    /// </summary>
    [Fact]
    public async Task ForgotPassword_ExceedingLimitFromSameClient_Returns429_ISSUE052()
    {
        var client = _factory.CreateClient();
        // Anonymous endpoint; the header just gives TenantResolutionMiddleware a real tenant so the handler
        // runs cleanly and the permitted requests return their normal 200.
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "platform");

        // The first N (= PermitLimit) requests must NOT be throttled.
        for (var i = 1; i <= ForgotPasswordLimit; i++)
        {
            var permitted = await client.PostAsJsonAsync(
                "/api/v1/auth/forgot-password",
                new { email = "rate-limit-probe@example.com" });

            permitted.StatusCode.Should().NotBe(
                HttpStatusCode.TooManyRequests,
                because: $"request #{i} is within the {ForgotPasswordLimit}/window limit and must pass the limiter");
        }

        // The (N+1)th request must be rejected by the limiter with 429.
        var rejected = await client.PostAsJsonAsync(
            "/api/v1/auth/forgot-password",
            new { email = "rate-limit-probe@example.com" });

        rejected.StatusCode.Should().Be(
            HttpStatusCode.TooManyRequests,
            because: $"request #{ForgotPasswordLimit + 1} exceeds the forgot-password limit and must be throttled (ISSUE-052)");
    }

    /// <summary>
    /// ISSUE-102 / US-REC-002: the anonymous public application-submit endpoint must throttle repeated
    /// requests from the same client. The first 10 requests pass the limiter (each 400s on the missing résumé
    /// file — irrelevant here, the permit is consumed before the action); the 11th is rejected with 429. Fails
    /// pre-fix because, without the <c>"public-application"</c> policy + attribute, the 11th also 400s rather
    /// than 429s.
    /// </summary>
    [Fact]
    public async Task PublicApplication_ExceedingLimitFromSameClient_Returns429_ISSUE102()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "platform");

        // Any GUID satisfies the {vacancyId:guid} route constraint so the endpoint is selected and its
        // rate-limit metadata is evaluated; an empty multipart body makes the action 400 (missing résumé),
        // which is a non-429 status — all we need for the "not throttled" arm.
        var applyUrl = $"/api/v1/careers/vacancies/{Guid.NewGuid()}/apply";

        for (var i = 1; i <= PublicApplicationLimit; i++)
        {
            var permitted = await client.PostAsync(applyUrl, new MultipartFormDataContent());

            permitted.StatusCode.Should().NotBe(
                HttpStatusCode.TooManyRequests,
                because: $"request #{i} is within the {PublicApplicationLimit}/window limit and must pass the limiter");
        }

        var rejected = await client.PostAsync(applyUrl, new MultipartFormDataContent());

        rejected.StatusCode.Should().Be(
            HttpStatusCode.TooManyRequests,
            because: $"request #{PublicApplicationLimit + 1} exceeds the public-application limit and must be throttled (ISSUE-102)");
    }
}
