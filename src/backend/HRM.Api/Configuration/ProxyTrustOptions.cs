using Microsoft.AspNetCore.HttpOverrides;

namespace HRM.Api.Configuration;

/// <summary>
/// BUG-308 — builds the <see cref="ForwardedHeadersOptions"/> for the reverse proxy in front of the API,
/// scoped to proxies configuration explicitly names.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not inline in Program.cs.</b> The single most important behaviour here is what happens
/// when NOTHING is configured, and that case is impossible to assert against a booted host without
/// standing up a second one. Extracting it makes the dangerous path directly testable.
/// </para>
/// <para>
/// <b>THE TRAP THIS EXISTS TO AVOID — measured, not theorised.</b> ASP.NET Core decides whether to perform
/// the known-proxy check with <c>KnownNetworks.Count &gt; 0 || KnownProxies.Count &gt; 0</c>. So clearing
/// both lists — the natural way to express "trust nobody" — instead means <b>no check is performed at
/// all</b>, and X-Forwarded-* is honoured from <b>every</b> caller. Empty is fail-OPEN, not fail-closed.
/// Verified against the running pipeline: with both lists empty, a forged
/// <c>X-Forwarded-Proto: https</c> from 203.0.113.9 produced <c>Strict-Transport-Security</c>.
/// </para>
/// <para>
/// Hence the contract: this returns <see langword="null"/> when no proxy is configured, and the caller
/// must then <b>not register the middleware at all</b>. That is the only way "unconfigured" genuinely
/// means "unchanged behaviour" rather than "trusts the whole internet".
/// </para>
/// </remarks>
public static class ProxyTrustOptions
{
    /// <summary>
    /// Builds options from the <c>Proxy</c> configuration section, or returns <see langword="null"/> when
    /// no trusted proxy is configured — in which case the middleware MUST NOT be registered.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A configured CIDR or IP is malformed. Deliberately loud: a typo that was silently skipped would look
    /// identical to a working proxy right up until someone wondered why HSTS never appeared in production.
    /// </exception>
    public static ForwardedHeadersOptions? Build(IConfiguration configuration)
    {
        var networks = configuration.GetSection("Proxy:KnownNetworks").Get<string[]>() ?? [];
        var proxies = configuration.GetSection("Proxy:KnownProxies").Get<string[]>() ?? [];

        if (networks.Length == 0 && proxies.Length == 0)
        {
            // Unconfigured => do not register. See the remarks above: an "empty" options object would
            // trust everyone, which is the opposite of what an unconfigured deployment wants.
            return null;
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor,
            // Must match the number of proxies actually in front of the app. Too high and a
            // client-supplied X-Forwarded-For entry can be mistaken for a proxy-appended one.
            ForwardLimit = configuration.GetValue<int?>("Proxy:ForwardLimit") ?? 1,
        };

        // ASP.NET Core pre-populates these with loopback. Clear first so the trusted set is exactly what
        // configuration names — never a silent union with a framework default.
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();

        foreach (var cidr in networks)
        {
            if (!System.Net.IPNetwork.TryParse(cidr, out var parsed))
            {
                throw new InvalidOperationException(
                    $"Proxy:KnownNetworks contains '{cidr}', which is not a valid CIDR (e.g. \"172.18.0.0/16\").");
            }

            // TryParse ACCEPTS host bits and silently normalises them: "10.1.2.3/16" parses happily to
            // 10.1.0.0/16. Verified, not assumed. On a TRUST LIST that silence is dangerous -- someone
            // meaning a single proxy host who typos the prefix length would widen trust to 65k addresses
            // and get no warning. Reject the ambiguity and make them write what they mean.
            if (!parsed.BaseAddress.Equals(System.Net.IPAddress.Parse(cidr.Split('/')[0])))
            {
                throw new InvalidOperationException(
                    $"Proxy:KnownNetworks contains '{cidr}', which has host bits set beyond its /{parsed.PrefixLength} "
                    + $"prefix. Write the network address ('{parsed.BaseAddress}/{parsed.PrefixLength}') if you meant "
                    + $"the whole range, or '/32' if you meant that single host -- a silently widened trust list is "
                    + "how one typo'd prefix length ends up trusting an entire subnet.");
            }

            options.KnownNetworks.Add(new IPNetwork(parsed.BaseAddress, parsed.PrefixLength));
        }

        foreach (var ip in proxies)
        {
            if (!System.Net.IPAddress.TryParse(ip, out var proxyIp))
            {
                throw new InvalidOperationException(
                    $"Proxy:KnownProxies contains '{ip}', which is not a valid IP address.");
            }

            options.KnownProxies.Add(proxyIp);
        }

        return options;
    }
}
