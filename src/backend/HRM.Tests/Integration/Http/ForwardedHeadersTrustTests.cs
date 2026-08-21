// ============================================================================
// BUG-308 — X-Forwarded-Proto is honoured from a KNOWN proxy and ignored from anyone else.
//
// The bug: TLS terminates at the reverse-proxy nginx, which forwards to backend:5000 over PLAIN HTTP with
// X-Forwarded-Proto: https. With no ForwardedHeaders middleware, ctx.Request.IsHttps was FALSE inside the
// API in the only deployment that has TLS — so the §23.4 HSTS branch was dead code, and the four
// scheme-derived branding-asset base URLs (AuthController.ToServableLogos, TenantContextController,
// TenantSettingsController x2) emitted http:// logo URLs on an https:// page, which browsers block as
// mixed content. (Reset/invite/portal links and the SSO redirect URI hardcode https:// from config and
// were never affected — an earlier draft of this comment said they were; that was verified false.)
//
// THE SCOPING IS THE SECURITY CONTROL. X-Forwarded-Proto is an ordinary request header that any caller can
// send. An UNSCOPED ForwardedHeaders middleware therefore lets an attacker forge Request.IsHttps — so the
// arm that matters here is not "does the proxy work" but "is everyone ELSE refused."
//
// HARNESS TRAP, MEASURED. TestServer never populates a socket peer address, and ForwardedHeadersMiddleware
// only runs its known-proxy check when it HAS one. Against the bare harness a spoofed X-Forwarded-Proto
// produced Strict-Transport-Security — i.e. the spoof SUCCEEDED. A rejection test written without a peer
// address would have been green while proving nothing. ApiTestFactory.TestPeerIpStartupFilter supplies the
// missing input; see its docs for why that is not a cheat.
//
// Test config comes from appsettings.Development.json: Proxy:KnownNetworks = [ "172.16.0.0/12" ].
// ============================================================================

using FluentAssertions;

namespace HRM.Tests.Integration.Http;

[Collection("HttpApi")]
public sealed class ForwardedHeadersTrustTests
{
    // The trusted network is pinned by ApiTestFactory to 172.18.0.0/16 -- NOT inherited from
    // appsettings.Development.json, so a dev-ergonomics edit there cannot silently widen what these
    // security arms accept.
    private const string TrustedProxyIp = "172.18.0.7";       // comfortably inside /16
    private const string UntrustedPeerIp = "203.0.113.9";     // TEST-NET-3, far outside

    // BOUNDARY PAIR. Without these, a prefix-length regression (/16 -> /12, or -> /8) survives every arm,
    // because 172.18.0.7 is inside all of them and 203.0.113.9 is outside all of them. These two sit one
    // address either side of the /16 edge and are what actually pins the prefix LENGTH.
    private const string JustInsideTrusted = "172.18.255.255";  // last address of 172.18.0.0/16
    private const string JustOutsideTrusted = "172.19.0.0";     // first address after it

    private readonly ApiTestFactory _factory;

    public ForwardedHeadersTrustTests(ApiTestFactory factory) => _factory = factory;

    private async Task<HttpResponseMessage> GetHealthAsync(string peerIp, string? forwardedProto)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Test-Peer-Ip", peerIp);
        if (forwardedProto is not null)
        {
            request.Headers.Add("X-Forwarded-Proto", forwardedProto);
        }

        return await client.SendAsync(request);
    }

    /// <summary>
    /// A forged X-Forwarded-Proto from a peer outside the configured proxy network must be discarded —
    /// otherwise any caller can make the app believe it is on HTTPS.
    ///
    /// READ THIS BEFORE TRUSTING THIS ARM ALONE. It asserts an ABSENCE, and an absence assertion is green
    /// in TWO different worlds: the one where the control correctly rejected the spoof, and the one where
    /// the middleware never ran at all. Empty the proxy config and this arm stays green — vacuously.
    /// The arm that actually catches that is the POSITIVE one below
    /// (<see cref="ForwardedProto_FromKnownProxy_IsHonoured_BUG308"/>), which goes red the moment the
    /// middleware stops being registered. The pair is the guard; neither half is sufficient alone.
    /// </summary>
    [Fact]
    public async Task SpoofedForwardedProto_FromUntrustedPeer_IsIgnored_BUG308()
    {
        var response = await GetHealthAsync(UntrustedPeerIp, "https");

        response.Headers.Contains("Strict-Transport-Security").Should().BeFalse(
            "X-Forwarded-Proto is a client-settable header; honouring it from an unknown peer would let "
            + "anyone forge Request.IsHttps. 203.0.113.9 is outside the configured 172.16.0.0/12 proxy "
            + "network, so the header must be stripped before it reaches the §23.4 middleware");
    }

    /// <summary>
    /// The positive case: from inside the configured proxy network the header IS honoured, which is what
    /// makes HSTS reachable in the containerised TLS deployment at all.
    /// </summary>
    [Fact]
    public async Task ForwardedProto_FromKnownProxy_IsHonoured_BUG308()
    {
        var response = await GetHealthAsync(TrustedProxyIp, "https");

        // PRESENCE only, deliberately. The exact max-age belongs to GAP-033a and is pinned by
        // SecurityHeadersApiTests; asserting it here would redden a proxy-TRUST arm whenever the
        // security-HEADERS module changed an unrelated value.
        response.Headers.Contains("Strict-Transport-Security").Should().BeTrue(
            "the proxy terminates TLS and forwards plain HTTP, so X-Forwarded-Proto from INSIDE the "
            + "known network is the only signal that the client connection was actually secure");
    }

    /// <summary>
    /// No forwarded header at all, from a trusted peer, must NOT invent HTTPS — the middleware promotes the
    /// scheme only when the proxy actually says so. Guards against a fix that keys on "is this a proxy?"
    /// rather than "what did the proxy report?".
    /// </summary>
    [Fact]
    public async Task NoForwardedProto_FromKnownProxy_StaysHttp_BUG308()
    {
        var response = await GetHealthAsync(TrustedProxyIp, forwardedProto: null);

        response.Headers.Contains("Strict-Transport-Security").Should().BeFalse(
            "trusting a proxy means trusting what it REPORTS, not assuming TLS because the peer is a proxy");
    }

    /// <summary>
    /// An explicit http report from the known proxy must also stay http. Together with the arm above this
    /// pins the promotion to the header's VALUE, not merely its presence.
    /// </summary>
    [Fact]
    public async Task ForwardedProtoHttp_FromKnownProxy_StaysHttp_BUG308()
    {
        var response = await GetHealthAsync(TrustedProxyIp, "http");

        response.Headers.Contains("Strict-Transport-Security").Should().BeFalse(
            "the proxy reported a plain-HTTP client connection, so RFC 6797 says do not send HSTS");
    }

    /// <summary>
    /// Pins the prefix LENGTH, which no other arm does. The last address inside 172.18.0.0/16 must be
    /// trusted and the first address after it must not — so widening to /12 or narrowing to /24 fails here
    /// even though every other arm would stay green.
    /// </summary>
    [Fact]
    public async Task TrustBoundary_IsExactlyThePrefixLength_NotJustTheObviousCases_BUG308()
    {
        var inside = await GetHealthAsync(JustInsideTrusted, "https");
        var outside = await GetHealthAsync(JustOutsideTrusted, "https");

        inside.Headers.Contains("Strict-Transport-Security").Should().BeTrue(
            $"{JustInsideTrusted} is the last address of the configured /16 and must be trusted");
        outside.Headers.Contains("Strict-Transport-Security").Should().BeFalse(
            $"{JustOutsideTrusted} is the first address OUTSIDE the configured /16; if this passes, the "
            + "trusted range has been silently widened beyond what was configured");
    }

    /// <summary>
    /// X-Forwarded-For is rejected from an untrusted peer too — the header the rate-limit partition key and
    /// the whole audit-IP trail depend on.
    ///
    /// WHY ASSERTING ON HSTS PROVES SOMETHING ABOUT X-Forwarded-For: ForwardedHeadersMiddleware makes ONE
    /// trust decision per request, then applies (or abandons) the whole forwarded set together — it does not
    /// evaluate each header separately. So a request carrying BOTH headers from an untrusted peer, whose
    /// proto was demonstrably not applied, also had its XFF discarded.
    ///
    /// HONEST LIMIT: this proves REJECTION covers XFF. It does NOT prove the accepted case rewrites
    /// RemoteIpAddress, because no endpoint echoes the resolved client IP. That residual is filed rather
    /// than papered over — see ISSUE-385.
    /// </summary>
    [Fact]
    public async Task SpoofedForwardedFor_FromUntrustedPeer_IsRejectedWithTheWholeSet_BUG308()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Test-Peer-Ip", UntrustedPeerIp);
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "10.9.9.9");

        var response = await client.SendAsync(request);

        response.Headers.Contains("Strict-Transport-Security").Should().BeFalse(
            "the forwarded set is trusted or abandoned as a unit, so an unapplied proto from this peer "
            + "means the accompanying X-Forwarded-For was discarded too — which is what stops a caller "
            + "forging the client IP that rate limiting and the audit trail record");
    }
}
