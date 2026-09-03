namespace HRM.Infrastructure.Identity;

/// <summary>
/// Strongly-typed binding of the <c>Jwt</c> configuration section, extended into a signing
/// <b>key ring</b> to support zero-downtime RSA signing-key rotation with overlap.
/// <para>
/// Rotation procedure (all config-driven, applied on the next restart — live rotation is out of scope):
/// <list type="number">
///   <item>Deploy the <b>new</b> key's PUBLIC PEM into <see cref="ValidationKeys"/> (with its own kid).
///         Tokens are still signed by the old <see cref="PrivateKey"/>; the new key is now accepted for
///         validation but not yet used to sign.</item>
///   <item>Promote: set <see cref="PrivateKey"/> to the new key and its <see cref="SigningKeyId"/> to the
///         new kid, and move the <b>old</b> key's PUBLIC PEM into <see cref="ValidationKeys"/>. New tokens
///         are now signed by the new key; access tokens already issued under the old key (15-min TTL)
///         keep validating until they expire.</item>
///   <item>After the old access-token TTL has elapsed, drop the old key's entry from
///         <see cref="ValidationKeys"/>.</item>
/// </list>
/// </para>
/// </summary>
public sealed class JwtKeyRingOptions
{
    public string? Issuer { get; set; }
    public string? Audience { get; set; }

    /// <summary>
    /// Primary signing key PEM. Blank => an ephemeral random RSA-2048 key is generated (dev/test default).
    /// That fallback is permitted <b>only</b> in the Development environment: <see cref="JwtSigningKeyStartupGuard"/>
    /// fails startup anywhere else, because a per-process key invalidates all issued tokens on restart and
    /// makes multi-instance deployments reject each other's tokens.
    /// </summary>
    public string? PrivateKey { get; set; }

    /// <summary>The <c>kid</c> stamped on tokens signed by <see cref="PrivateKey"/>.</summary>
    public string SigningKeyId { get; set; } = "hrm-dev-key-1";

    /// <summary>Extra PUBLIC keys accepted during a rotation overlap window.</summary>
    public List<JwtValidationKey> ValidationKeys { get; set; } = new();
}

/// <summary>A single extra public validation key in the ring, keyed by its <c>kid</c>.</summary>
public sealed class JwtValidationKey
{
    public string Kid { get; set; } = "";
    public string PublicKeyPem { get; set; } = "";
}
