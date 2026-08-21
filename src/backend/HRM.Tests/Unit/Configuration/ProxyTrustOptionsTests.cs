// ============================================================================
// BUG-308 — the unconfigured case, which no booted-host test can reach.
//
// THE BUG THESE EXIST TO PREVENT WAS REAL AND WAS MINE. The first cut of this change put empty
// KnownNetworks/KnownProxies in appsettings.json and called it a "fail-safe default", with a comment
// saying empty meant the middleware trusted nobody. That was backwards. ASP.NET Core gates its
// known-proxy check on `KnownNetworks.Count > 0 || KnownProxies.Count > 0`, so BOTH lists empty means
// NO CHECK AT ALL and X-Forwarded-* is honoured from every caller.
//
// It was caught by running it, not by reading it: with both lists emptied, the integration arm
// SpoofedForwardedProto_FromUntrustedPeer_IsIgnored_BUG308 went RED -- a forged X-Forwarded-Proto from
// 203.0.113.9 produced Strict-Transport-Security. The fix is to not register the middleware at all when
// nothing is configured, which is exactly what the null return here encodes.
// ============================================================================

using FluentAssertions;
using HRM.Api.Configuration;
using Microsoft.Extensions.Configuration;

namespace HRM.Tests.Unit.Configuration;

public sealed class ProxyTrustOptionsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] entries)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    /// <summary>
    /// THE ARM THAT MATTERS. Nothing configured must yield null so the caller skips registration.
    /// Returning a configured-but-empty options object here would silently trust the entire internet.
    /// </summary>
    [Fact]
    public void Build_WithNoProxyConfigured_ReturnsNull_SoTheMiddlewareIsNotRegistered_BUG308()
    {
        var options = ProxyTrustOptions.Build(Config());

        options.Should().BeNull(
            "ASP.NET Core skips its known-proxy check when both lists are empty, so registering the "
            + "middleware unconfigured honours X-Forwarded-* from ANY caller. Unconfigured must mean "
            + "not registered, which is the only way it equals pre-BUG-308 behaviour");
    }

    [Fact]
    public void Build_WithAKnownNetwork_ReturnsOptionsCarryingExactlyThatNetwork_BUG308()
    {
        var options = ProxyTrustOptions.Build(Config(("Proxy:KnownNetworks:0", "172.18.0.0/16")));

        options.Should().NotBeNull();
        options!.KnownNetworks.Should().ContainSingle();
        options.KnownNetworks[0].Prefix.ToString().Should().Be("172.18.0.0");
        options.KnownNetworks[0].PrefixLength.Should().Be(16);
    }

    /// <summary>
    /// The framework pre-populates KnownNetworks/KnownProxies with loopback. If those survive, the trusted
    /// set is a silent union of config and framework defaults rather than exactly what was asked for.
    /// </summary>
    [Fact]
    public void Build_ClearsTheFrameworkDefaults_SoTrustIsExactlyWhatConfigNames_BUG308()
    {
        var options = ProxyTrustOptions.Build(Config(("Proxy:KnownProxies:0", "10.1.2.3")));

        options.Should().NotBeNull();
        options!.KnownProxies.Should().ContainSingle().Which.ToString().Should().Be("10.1.2.3");
        options.KnownNetworks.Should().BeEmpty(
            "the framework seeds loopback into KnownNetworks; leaving it would widen the trusted set "
            + "beyond what configuration names");
    }

    /// <summary>
    /// A typo must be loud. Silently skipping a malformed CIDR looks identical to a working proxy right up
    /// until someone wonders why HSTS never appears in production.
    /// </summary>
    [Theory]
    [InlineData("172.18.0.0")]        // no prefix length
    [InlineData("not-an-address/16")]
    [InlineData("172.18.0.5/16")]     // host bits set -- the strict parse rejects this
    public void Build_WithAMalformedNetwork_Throws_RatherThanSilentlySkipping_BUG308(string bad)
    {
        var act = () => ProxyTrustOptions.Build(Config(("Proxy:KnownNetworks:0", bad)));

        act.Should().Throw<InvalidOperationException>().WithMessage("*KnownNetworks*");
    }

    [Fact]
    public void Build_WithAMalformedProxyIp_Throws_BUG308()
    {
        var act = () => ProxyTrustOptions.Build(Config(("Proxy:KnownProxies:0", "10.1.2.999")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*KnownProxies*");
    }

    [Fact]
    public void Build_DefaultsForwardLimitToOne_AndHonoursAnOverride_BUG308()
    {
        ProxyTrustOptions.Build(Config(("Proxy:KnownProxies:0", "10.1.2.3")))!
            .ForwardLimit.Should().Be(1, "the default must match a single proxy hop");

        ProxyTrustOptions.Build(Config(
            ("Proxy:KnownProxies:0", "10.1.2.3"),
            ("Proxy:ForwardLimit", "2")))!
            .ForwardLimit.Should().Be(2);
    }
}
