using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HRM.Infrastructure.Identity;

/// <summary>
/// Startup fail-fast for the JWT signing key (queue item <c>G2</c>).
/// <para>
/// <see cref="JwtService"/> falls back to <c>RSA.Create(2048)</c> when <c>Jwt:PrivateKey</c> is blank. That
/// fallback is a deliberate local-development convenience and a production outage: the key lives only in the
/// process that generated it, so <b>every restart or redeploy invalidates every token already issued</b>, and
/// in a multi-instance deployment <b>each instance signs with a different key, so instances reject each
/// other's tokens</b>. Nothing about that failure is visible at startup — it surfaces later as users being
/// logged out mid-session and intermittent 401s behind a load balancer.
/// </para>
/// <para>
/// There is no later seam to catch it in: <see cref="JwtService"/> is a singleton constructed eagerly in
/// <c>Program.cs</c>, and the <c>TokenValidationParameters</c> handed to the JWT bearer handler are
/// snapshotted from it exactly once. There is no <c>IOptionsMonitor</c> and nothing re-reads configuration
/// afterwards, so this validation happens at startup or it happens never.
/// </para>
/// </summary>
public static class JwtSigningKeyStartupGuard
{
    /// <summary>
    /// Throws when a persistent signing key is required but absent. Call this from the composition root
    /// <b>before</b> constructing <see cref="JwtService"/>, so the process cannot reach a state where it is
    /// signing with an ephemeral key.
    /// </summary>
    /// <param name="configuration">
    /// The application configuration. Bound through the <b>same</b> path <see cref="JwtService"/> uses
    /// (<c>GetSection("Jwt").Get&lt;JwtKeyRingOptions&gt;()</c>) so the guard can never disagree with the
    /// service about whether a key is present.
    /// </param>
    /// <param name="environment">
    /// The <b>resolved</b> host environment — deliberately not the raw <c>ASPNETCORE_ENVIRONMENT</c> string.
    /// <see cref="IHostEnvironment.EnvironmentName"/> is the single authoritative resolution of the
    /// environment variable, the <c>--environment</c> switch and <c>IWebHostBuilder.UseEnvironment</c>
    /// (which the test hosts use, and which never sets <c>ASPNETCORE_ENVIRONMENT</c> at all). Reading the raw
    /// variable would make this guard blind to two of those three sources.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// No signing key is configured and the environment is not the one where the generated key is permitted.
    /// </exception>
    public static void EnsureSigningKeyIsConfigured(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var options = configuration.GetSection("Jwt").Get<JwtKeyRingOptions>();

        // A configured key is the normal case and needs no environment reasoning at all. Whitespace counts as
        // absent: a key of " " cannot sign anything, so treating it as "configured" would let the guard pass
        // and then fail obscurely inside RSA.ImportFromPem.
        if (!string.IsNullOrWhiteSpace(options?.PrivateKey))
            return;

        // ALLOW-LIST, not deny-list. The hazard is a property of the signing key, not of one environment
        // name: Staging, QA, Demo, a bespoke environment name, and an unset or misspelled one are all places
        // where an ephemeral per-process key produces exactly the outage described above. A deny-list
        // (`== "Production"`) permits every one of them, and — worse — its failure mode is correlated with the
        // fault it exists to catch, because whoever forgot to configure Jwt:PrivateKey is precisely the person
        // liable to have left the environment name unset too. So the generated key is permitted only where it
        // is explicitly asked for. The cost of being wrong here is a startup failure carrying instructions;
        // the cost of the opposite is silent, intermittent authentication failure in production.
        if (environment.IsDevelopment())
            return;

        throw new InvalidOperationException(
            "Jwt:PrivateKey is not configured, so JwtService would sign access and impersonation tokens with "
            + "an RSA key generated fresh in memory at startup. That key is per-process and never persisted, "
            + "which means: (1) every restart or redeploy INVALIDATES EVERY TOKEN ALREADY ISSUED, logging all "
            + "users out mid-session; and (2) in a multi-instance deployment each instance signs with a "
            + "DIFFERENT key, so INSTANCES REJECT EACH OTHER'S TOKENS and users see intermittent 401s behind "
            + "the load balancer. Neither failure is visible at startup, which is why this is checked here. "
            + $"The resolved environment is '{environment.EnvironmentName}'; the generated key is permitted "
            + "ONLY in 'Development'. Fix by configuring a persistent RSA private key PEM before starting — "
            + "e.g. Jwt__PrivateKey=\"$(openssl genrsa 2048)\" via the environment or user-secrets, together "
            + "with Jwt:SigningKeyId (JwtKeyRingOptions documents the rotation procedure). If this really is a "
            + "local development run, set ASPNETCORE_ENVIRONMENT=Development instead.");
    }
}
