// ============================================================================
// ISSUE-385 — the ACCEPTED half of X-Forwarded-For, end to end.
//
// BUG-308's suite proved the REJECTION half: a forged X-Forwarded-* from an untrusted peer is discarded.
// It could not prove the accepted half — that a TRUSTED proxy's X-Forwarded-For actually rewrites
// Connection.RemoteIpAddress — because no endpoint echoes the resolved client IP, so there was nothing to
// assert against. That gap was filed rather than papered over, and Program.cs's comment was corrected to
// stop citing the suite as covering it.
//
// WHY THIS IS NOW TESTABLE WITHOUT ADDING ANYTHING TO PRODUCTION. `AuditInterceptor.StampRequestContext`
// already persists `httpContext.Connection.RemoteIpAddress` into `audit_logs.ip_address` on any newly-added
// audit row. That is a real, pre-existing observable of the resolved client IP — so the accepted path can be
// asserted against the DATABASE rather than by inventing an echo endpoint purely to make a test possible.
// Adding a production endpoint to satisfy a test would have been coverage theatre.
//
// THE OBSERVABLE, chosen after checking rather than assuming: a SUCCESSFUL login stores the resolved client
// IP on the refresh token (AuthController passes HttpContext.Connection.RemoteIpAddress into the login
// command, which lands on RefreshToken.IpAddress). A FAILED login was tried first and writes no audit row at
// all, so it observed nothing -- the arms returned null and said so rather than passing vacuously.
//
// It matters beyond the header: refresh_tokens.ip_address and audit_logs.ip_address are the security trail.
// If X-Forwarded-For were mishandled, every row behind the proxy would record the proxy instead of the actor.
// ============================================================================

using System.Net.Http.Json;
using FluentAssertions;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration.Http;

[Collection("HttpApi")]
public sealed class ForwardedForClientIpApiTests
{
    // ApiTestFactory pins Proxy:KnownNetworks to 172.18.0.0/16.
    private const string TrustedProxyIp = "172.18.0.7";
    private const string UntrustedPeerIp = "203.0.113.9";

    // The address a proxy would forward on behalf of the real caller. Deliberately distinct from BOTH peers
    // above, so "the client IP was recorded" cannot be satisfied by either peer value by accident.
    private const string RealClientIp = "198.51.100.42";

    private readonly ApiTestFactory _factory;

    public ForwardedForClientIpApiTests(ApiTestFactory factory) => _factory = factory;

    private const string AdminEmail = "admin@hrm.local";
    private const string AdminPassword = "Admin@123!";

    /// <summary>
    /// The IP recorded on the refresh token minted by the most recent successful login.
    /// </summary>
    private async Task<string?> LatestLoginIpAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.RefreshTokens.IgnoreQueryFilters().AsNoTracking()
            .OrderByDescending(t => t.IssuedAt)
            .Select(t => t.IpAddress)
            .FirstOrDefaultAsync();
    }

    private async Task<HttpResponseMessage> LoginAsync(string peerIp, string? forwardedFor)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email = AdminEmail, password = AdminPassword }),
        };
        request.Headers.Add("X-Test-Peer-Ip", peerIp);
        request.Headers.Add("X-Tenant-Subdomain", "platform");
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        var response = await client.SendAsync(request);
        // A failed login mints no refresh token, so the arms would read a STALE row and could pass for the
        // wrong reason. Assert the login actually succeeded before trusting what follows.
        response.IsSuccessStatusCode.Should().BeTrue(
            $"the arms read the refresh token this login mints; a failed login would leave them asserting "
            + $"against a previous test's row. Body: {await response.Content.ReadAsStringAsync()}");
        return response;
    }

    /// <summary>
    /// THE ARM ISSUE-385 EXISTS FOR. From a peer inside the configured proxy network, X-Forwarded-For must
    /// actually rewrite the resolved client IP — proven by reading the value the audit trail persisted.
    ///
    /// Without this, a regression that silently stopped honouring X-Forwarded-For would leave every request
    /// behind the proxy sharing the proxy's address: one rate-limit bucket for all tenants, and an audit trail
    /// that records the infrastructure instead of the actor. The BUG-308 suite would have stayed green
    /// throughout, because it only ever proved the rejection half.
    /// </summary>
    [Fact]
    public async Task ForwardedFor_FromAKnownProxy_BecomesTheRecordedClientIp_ISSUE385()
    {
        await LoginAsync(TrustedProxyIp, RealClientIp);

        var recorded = await LatestLoginIpAsync();

        recorded.Should().Be(RealClientIp,
            "the proxy forwards on behalf of the real caller, so a trusted X-Forwarded-For must replace the "
            + "socket peer — otherwise every request behind the proxy shares one rate-limit bucket and the "
            + "audit trail records the infrastructure rather than the actor");
        recorded.Should().NotBe(TrustedProxyIp, "recording the proxy's own address is the bug, not the fix");
    }

    /// <summary>
    /// The mirror, at the same observable: from an UNTRUSTED peer the header must be discarded and the socket
    /// peer recorded instead. Together these pin the trust boundary at the point where the value is actually
    /// used, not merely where it is computed.
    ///
    /// This is also the arm that stops the fix becoming a forgery channel — if a forged X-Forwarded-For could
    /// reach the audit trail, an attacker could write someone else's address into the security log.
    /// </summary>
    [Fact]
    public async Task ForwardedFor_FromAnUntrustedPeer_IsIgnored_AndTheSocketPeerIsRecorded_ISSUE385()
    {
        await LoginAsync(UntrustedPeerIp, RealClientIp);

        var recorded = await LatestLoginIpAsync();

        recorded.Should().Be(UntrustedPeerIp,
            "an untrusted caller must not be able to choose what the audit trail records about them — "
            + "honouring this header here would turn the security log into a forgery channel");
        recorded.Should().NotBe(RealClientIp, "the forged header must not reach the audit trail");
    }

    /// <summary>
    /// No forwarded header at all: the socket peer is recorded. Guards against a fix that keys on "is this a
    /// trusted proxy?" rather than "what did the proxy actually report?".
    /// </summary>
    [Fact]
    public async Task NoForwardedFor_FromAKnownProxy_RecordsTheProxyItself_ISSUE385()
    {
        await LoginAsync(TrustedProxyIp, forwardedFor: null);

        var recorded = await LatestLoginIpAsync();

        recorded.Should().Be(TrustedProxyIp,
            "trusting a proxy means trusting what it REPORTS; with nothing reported there is no client "
            + "address to substitute, so the peer stands");
    }
}
