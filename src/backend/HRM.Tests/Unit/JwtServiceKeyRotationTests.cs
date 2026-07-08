// ============================================================================
// Phase 1c — JWT signing-key rotation / overlap window.
//
// JwtService (RS256/RSA-2048) gains a key-ring: it SIGNS with one primary RSA
// key (Jwt:PrivateKey, stamped with kid = Jwt:SigningKeyId) but VALIDATES against
// a COLLECTION of public keys (the primary plus Jwt:ValidationKeys[]). During a
// rotation overlap window a token signed by a now-retired key must still validate,
// while a token signed by a key that is in NO ring must be rejected.
//
// These are pure crypto unit tests: RSA keypairs are generated in-test with
// RSA.Create(2048) and exported to PEM to feed IConfiguration exactly as prod
// would. No mocks of JwtService, no InMemory/Postgres, no Docker — real signing
// and real validation. A token that must not validate is asserted to be rejected.
//
// Config shape consumed (matches Phase-1c spec):
//   Jwt:PrivateKey                    → primary signing key PEM (blank => random dev key)
//   Jwt:SigningKeyId                  → kid stamped on tokens signed by PrivateKey (default "hrm-dev-key-1")
//   Jwt:ValidationKeys:{i}:Kid        → extra accepted public key id
//   Jwt:ValidationKeys:{i}:PublicKeyPem → extra accepted RSA PUBLIC key PEM
//   Jwt:Issuer / Jwt:Audience         → shared across services so cross-service validation is meaningful
// ============================================================================

using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using FluentAssertions;
using HRM.Domain.Entities;
using HRM.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;

namespace HRM.Tests.Unit;

public sealed class JwtServiceKeyRotationTests
{
    // Shared issuer/audience so a token minted by one service is a legitimate candidate for
    // validation by another — isolating the SIGNING-KEY dimension as the only thing under test.
    private const string Issuer = "hrm-api-rotation-test";
    private const string Audience = "hrm-client-rotation-test";

    private const string KidA = "hrm-key-A";
    private const string KidB = "hrm-key-B";
    private const string KidC = "hrm-key-C";
    private const string DefaultKid = "hrm-dev-key-1";

    private readonly User _user = new()
    {
        Id = Guid.NewGuid(),
        Email = "rotation.user@acme.test",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
    };

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userTenantId = Guid.NewGuid();
    private static readonly string[] Roles = { "HR Manager" };
    private static readonly string[] Permissions = { "Employees.View" };

    private string IssueToken(JwtService svc) =>
        svc.GenerateAccessToken(_user, _tenantId, _userTenantId, Roles, Permissions);

    // ── 1. Baseline single key ────────────────────────────────────────────────
    // One RSA key, explicit kid. The same service that signed must validate.
    [Fact]
    public void Baseline_SingleKey_IssuesAndValidates()
    {
        var keyA = RSA.Create(2048);
        var svc = new JwtService(BuildConfig(privateKeyPem: keyA.ExportRSAPrivateKeyPem(), signingKeyId: KidA));

        var token = IssueToken(svc);

        svc.ValidateAccessToken(token)
            .Should().NotBeNull("a service must validate a token it just signed with its own primary key");
    }

    // ── 2. Overlap — old token still valid after rotation ─────────────────────
    // Service A (primary = keyA) mints a token. Service B has ROTATED to primary = keyB but keeps
    // keyA's PUBLIC key in its validation ring. B must validate BOTH the retired-key token and a fresh one.
    [Fact]
    public void Overlap_OldKeyTokenStillValidatesAfterRotation()
    {
        var keyA = RSA.Create(2048);
        var keyB = RSA.Create(2048);

        var serviceA = new JwtService(BuildConfig(privateKeyPem: keyA.ExportRSAPrivateKeyPem(), signingKeyId: KidA));
        var tokenSignedByA = IssueToken(serviceA);

        // Rotated service: signs with keyB, but still ACCEPTS keyA via the validation ring (the overlap window).
        var serviceB = new JwtService(BuildConfig(
            privateKeyPem: keyB.ExportRSAPrivateKeyPem(),
            signingKeyId: KidB,
            validationKeys: new[] { (KidA, keyA.ExportSubjectPublicKeyInfoPem()) }));

        serviceB.ValidateAccessToken(tokenSignedByA)
            .Should().NotBeNull("during the overlap window a token signed by the retired key A must still validate");

        var tokenSignedByB = IssueToken(serviceB);
        serviceB.ValidateAccessToken(tokenSignedByB)
            .Should().NotBeNull("the rotated service must also validate tokens signed by its new primary key B");
    }

    // ── 3. Rejection of an unknown key ────────────────────────────────────────
    // A token signed by keyC (in NO ring on service B) must be rejected — the ring is an allow-list, not a bypass.
    [Fact]
    public void UnknownKey_TokenIsRejected()
    {
        var keyA = RSA.Create(2048);
        var keyB = RSA.Create(2048);
        var keyC = RSA.Create(2048);

        var serviceC = new JwtService(BuildConfig(privateKeyPem: keyC.ExportRSAPrivateKeyPem(), signingKeyId: KidC));
        var tokenSignedByC = IssueToken(serviceC);

        // Ring holds keyB (primary) + keyA (overlap) — but NOT keyC.
        var serviceB = new JwtService(BuildConfig(
            privateKeyPem: keyB.ExportRSAPrivateKeyPem(),
            signingKeyId: KidB,
            validationKeys: new[] { (KidA, keyA.ExportSubjectPublicKeyInfoPem()) }));

        serviceB.ValidateAccessToken(tokenSignedByC)
            .Should().BeNull("a token signed by a key that is in no validation ring must be rejected");
    }

    // ── 4. Back-compat / legacy path ──────────────────────────────────────────
    // Only Jwt:PrivateKey set — no SigningKeyId, no ValidationKeys. Issues + validates exactly as before,
    // and the kid defaults to "hrm-dev-key-1".
    [Fact]
    public void LegacyConfig_OnlyPrivateKey_IssuesValidatesAndDefaultsKid()
    {
        var keyA = RSA.Create(2048);
        var svc = new JwtService(BuildConfig(privateKeyPem: keyA.ExportRSAPrivateKeyPem()));

        var token = IssueToken(svc);

        svc.ValidateAccessToken(token)
            .Should().NotBeNull("legacy single-key config must issue and validate tokens exactly as before");

        new JwtSecurityTokenHandler().ReadJwtToken(token).Header.Kid
            .Should().Be(DefaultKid, "with no SigningKeyId the stamped kid must default to hrm-dev-key-1");
    }

    // ── 5. Header kid reflects the configured SigningKeyId ─────────────────────
    [Fact]
    public void IssuedToken_CarriesConfiguredSigningKeyId_AsKid()
    {
        var keyA = RSA.Create(2048);
        var svc = new JwtService(BuildConfig(privateKeyPem: keyA.ExportRSAPrivateKeyPem(), signingKeyId: KidA));

        var token = IssueToken(svc);

        new JwtSecurityTokenHandler().ReadJwtToken(token).Header.Kid
            .Should().Be(KidA, "the token header kid must be the configured Jwt:SigningKeyId so validators can select the right key");
    }

    // ── Config builder ────────────────────────────────────────────────────────
    private static IConfiguration BuildConfig(
        string privateKeyPem,
        string? signingKeyId = null,
        (string Kid, string PublicKeyPem)[]? validationKeys = null)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
            ["Jwt:PrivateKey"] = privateKeyPem,
        };

        if (signingKeyId is not null)
            dict["Jwt:SigningKeyId"] = signingKeyId;

        if (validationKeys is not null)
        {
            for (var i = 0; i < validationKeys.Length; i++)
            {
                dict[$"Jwt:ValidationKeys:{i}:Kid"] = validationKeys[i].Kid;
                dict[$"Jwt:ValidationKeys:{i}:PublicKeyPem"] = validationKeys[i].PublicKeyPem;
            }
        }

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }
}
