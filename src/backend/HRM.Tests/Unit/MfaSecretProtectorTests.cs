// ============================================================================
// IEEE-829 regression suite — MFA-secret encryption at rest (US-AUTH-005 NFR-2).
//
// Landed change under test (branch fix/auth-hardening-b):
//   * IFieldProtector + MfaSecretProtector (DataProtection, purpose "HRM.MfaSecret.v1")
//     + PlaintextFieldProtector.Instance no-op fallback.
//   * AuthService.EnrollMfaAsync now Protects User.MfaSecret; the verify paths
//     (VerifyMfaEnrollmentAsync / VerifyMfaLoginAsync / LoginAsync) Unprotect it.
//   * Unprotect tolerates legacy plaintext (returns non-protected input unchanged).
//
// Pre-fix these tests fail because the raw TOTP secret was stored verbatim in
// mfa_secret — a DB read disclosed it, so the "stored value != plaintext" assertion
// could not hold and there was no protector to round-trip.
//
// Provider: real ASP.NET Core DataProtection + real TotpService + EF InMemory
// through the real AuthService (the encrypt/decrypt seam is provider-independent).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Identity;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Security;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OtpNet;

namespace HRM.Tests.Unit;

public sealed class MfaSecretProtectorTests
{
    // -------- TC-AUTH-ENC-01: MfaSecretProtector round-trips a real DataProtection payload --------
    [Fact]
    public void MfaProtector_RoundTrip_AUTH005()
    {
        var protector = new MfaSecretProtector(DataProtectionProvider.Create("tests"));
        const string secret = "JBSWY3DPEHPK3PXP"; // a base32 TOTP secret

        var stored = protector.Protect(secret);

        stored.Should().NotBe(secret, "a raw DB read must not disclose the plaintext secret");
        protector.Unprotect(stored).Should().Be(secret, "Unprotect must recover the exact plaintext");
    }

    // -------- TC-AUTH-ENC-02: Unprotect of never-protected (legacy plaintext) passes through --------
    // This is what keeps pre-encryption enrollments verifying after the change ships.
    [Fact]
    public void MfaProtector_LegacyPlaintext_PassThrough()
    {
        var protector = new MfaSecretProtector(DataProtectionProvider.Create("tests"));
        const string legacyPlaintext = "JBSWY3DPEHPK3PXP"; // stored before encryption existed

        // Must not throw (CryptographicException/FormatException are caught) and must return as-is.
        var act = () => protector.Unprotect(legacyPlaintext);

        act.Should().NotThrow();
        protector.Unprotect(legacyPlaintext).Should().Be(legacyPlaintext);
    }

    // A payload protected by a DIFFERENT key ring must also degrade gracefully (legacy-tolerance path),
    // proving the fallback is exercised by a genuinely undecryptable-but-plausible value, not only a
    // non-base64url string.
    [Fact]
    public void MfaProtector_ForeignKeyRing_PassThrough()
    {
        var alien = new MfaSecretProtector(DataProtectionProvider.Create("some-other-app"));
        var mine = new MfaSecretProtector(DataProtectionProvider.Create("tests"));

        var protectedByAlien = alien.Protect("JBSWY3DPEHPK3PXP");

        // Different key ring -> cannot decrypt -> returned unchanged rather than throwing.
        mine.Unprotect(protectedByAlien).Should().Be(protectedByAlien);
    }

    // -------- TC-AUTH-ENC-03: AuthService enroll stores an ENCRYPTED secret, verify still round-trips --------
    // Injects a REAL MfaSecretProtector (not the no-op fallback) so the encrypt path is actually exercised,
    // then proves a TOTP code computed from the enroll-response secret still verifies through the real
    // decrypt path — i.e. encrypt-then-decrypt survives the full enroll -> verify flow.
    [Fact]
    public async Task MfaEnroll_StoresEncrypted_VerifyStillWorks_AUTH005()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        tenantContext.IsResolved.Returns(true);
        tenantContext.IsSystemContext.Returns(false);

        AppDbContext Db() => TestDbContextFactory.Create(tenantContext, dbName);

        // Seed an active user with NO MFA yet.
        using (var seed = Db())
        {
            seed.Users.Add(new User
            {
                Id = userId,
                Email = "mfa-user@acme.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Whatever0nly!", workFactor: 12),
                IsActive = true,
                MfaEnabled = false,
                MfaSecret = null,
            });
            await seed.SaveChangesAsync();
        }

        var protector = new MfaSecretProtector(DataProtectionProvider.Create("tests"));
        var totp = new TotpService(); // real TOTP so ValidateCode runs the genuine RFC-6238 path

        var service = CreateAuthService(Db(), tenantContext, totp, protector);

        // Enroll: returns the RAW secret to the client, but should PERSIST an encrypted form.
        var enroll = await service.EnrollMfaAsync(userId);
        enroll.IsSuccess.Should().BeTrue();
        var rawSecret = enroll.Value!.Secret;
        rawSecret.Should().NotBeNullOrWhiteSpace();

        // Persisted value must NOT be the raw secret, and must decrypt back to it.
        string stored;
        using (var check = Db())
        {
            stored = (await check.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId)).MfaSecret!;
        }
        stored.Should().NotBe(rawSecret, "the TOTP secret must be encrypted at rest (US-AUTH-005 NFR-2)");
        protector.Unprotect(stored).Should().Be(rawSecret, "the stored value must decrypt to the enrolled secret");

        // A TOTP code computed from the enroll-response secret must still verify — proving the verify path
        // decrypts the stored secret before validating.
        var code = new Totp(Base32Encoding.ToBytes(rawSecret), step: 30, mode: OtpHashMode.Sha1, totpSize: 6)
            .ComputeTotp();

        var verify = await service.VerifyMfaEnrollmentAsync(userId, code);
        verify.IsSuccess.Should().BeTrue("a code from the raw secret must verify against the decrypted stored secret");

        using var final = Db();
        (await final.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId))
            .MfaEnabled.Should().BeTrue();
    }

    // ========================================================================
    // US-PLT-005 (Scope A) — IsProtected detection + the legacy back-fill that upgrades plaintext rows.
    // ========================================================================

    // -------- TC-PLT-005-01: IsProtected TRUE for a real Protect() output --------
    // Paired with the FALSE theory below, this pins DISCRIMINATION: a hard-coded `false` fails here, a
    // hard-coded `true` fails the theory — neither constant can satisfy both arms.
    [Fact]
    public void IsProtected_ReturnsTrue_ForProtectedOutput_PLT005()
    {
        var protector = new MfaSecretProtector(DataProtectionProvider.Create("tests"));

        var stored = protector.Protect("JBSWY3DPEHPK3PXP");

        protector.IsProtected(stored).Should().BeTrue("a value this protector produced is not legacy plaintext");
    }

    // -------- TC-PLT-005-02: IsProtected FALSE for anything this protector did not produce --------
    [Theory]
    [InlineData("JBSWY3DPEHPK3PXP")]         // a raw base32 TOTP secret (the real legacy shape)
    [InlineData("")]                          // empty — not a valid protected payload
    [InlineData("not-base64url-!@#$ junk")]  // arbitrary non-base64url junk
    public void IsProtected_ReturnsFalse_ForLegacyPlaintext_PLT005(string legacy)
    {
        var protector = new MfaSecretProtector(DataProtectionProvider.Create("tests"));

        protector.IsProtected(legacy).Should().BeFalse(
            "legacy plaintext (undecryptable by this protector) must be reported as NOT protected");
    }

    // -------- TC-PLT-005-03: a foreign-key-ring payload is also "not protected" (mirrors Unprotect's tolerance) --------
    // IsProtected must agree with Unprotect about what "legacy" means: a value Unprotect would pass through
    // (foreign key ring) must be reported as unprotected here, or the back-fill and the auth path would disagree.
    [Fact]
    public void IsProtected_ReturnsFalse_ForForeignKeyRingPayload_PLT005()
    {
        var alien = new MfaSecretProtector(DataProtectionProvider.Create("some-other-app"));
        var mine = new MfaSecretProtector(DataProtectionProvider.Create("tests"));

        var protectedByAlien = alien.Protect("JBSWY3DPEHPK3PXP");

        mine.IsProtected(protectedByAlien).Should().BeFalse();
        mine.Unprotect(protectedByAlien).Should().Be(protectedByAlien, "the two must never disagree about 'legacy'");
    }

    // -------- TC-PLT-005-04: PlaintextFieldProtector.IsProtected is always false (it protects nothing) --------
    [Fact]
    public void PlaintextProtector_IsProtected_AlwaysFalse_PLT005()
    {
        PlaintextFieldProtector.Instance.IsProtected("anything").Should().BeFalse();
        PlaintextFieldProtector.Instance.IsProtected("").Should().BeFalse();
    }

    // -------- TC-PLT-005-05: the back-fill upgrades a legacy plaintext row LOSSLESSLY (real EF change-tracking) --------
    // Load-bearing: after the upgrade the stored value must (a) no longer be plaintext and (b) still Unprotect to
    // the ORIGINAL secret — proving the row can still authenticate. Uses InMemory (mfa_secret has NO value
    // converter, so InMemory holds a genuine raw plaintext form) exercised through the REAL AppDbContext change
    // tracker; the real-Postgres arm lives in MfaSecretBackfillPostgresTests.
    [Fact]
    public async Task Backfill_UpgradesLegacyPlaintextRow_Lossless_PLT005()
    {
        var dbName = Guid.NewGuid().ToString();
        var protector = new MfaSecretProtector(DataProtectionProvider.Create("tests"));
        const string legacySecret = "JBSWY3DPEHPK3PXP";
        var userId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(Guid.NewGuid(), dbName))
        {
            seed.Users.Add(new User { Id = userId, Email = "legacy@acme.com", MfaEnabled = true, MfaSecret = legacySecret });
            await seed.SaveChangesAsync();
        }

        using (var run = TestDbContextFactory.Create(Guid.NewGuid(), dbName))
        {
            await DbInitializer.BackfillLegacyMfaSecretsAsync(run, protector, NullLogger.Instance, CancellationToken.None);
        }

        using var check = TestDbContextFactory.Create(Guid.NewGuid(), dbName);
        var stored = (await check.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId)).MfaSecret!;
        stored.Should().NotBe(legacySecret, "the legacy plaintext secret must be encrypted at rest after the back-fill");
        protector.IsProtected(stored).Should().BeTrue();
        protector.Unprotect(stored).Should().Be(legacySecret,
            "the upgrade must be lossless — the user must still authenticate with the same TOTP secret");
    }

    // -------- TC-PLT-005-06: the back-fill is idempotent — a second run never double-wraps --------
    [Fact]
    public async Task Backfill_IsIdempotent_NoDoubleWrap_PLT005()
    {
        var dbName = Guid.NewGuid().ToString();
        var protector = new MfaSecretProtector(DataProtectionProvider.Create("tests"));
        const string legacySecret = "JBSWY3DPEHPK3PXP";
        var userId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(Guid.NewGuid(), dbName))
        {
            seed.Users.Add(new User { Id = userId, Email = "idem@acme.com", MfaEnabled = true, MfaSecret = legacySecret });
            await seed.SaveChangesAsync();
        }

        // First run: upgrades the row.
        using (var run1 = TestDbContextFactory.Create(Guid.NewGuid(), dbName))
        {
            await DbInitializer.BackfillLegacyMfaSecretsAsync(run1, protector, NullLogger.Instance, CancellationToken.None);
        }

        string afterFirst;
        using (var check1 = TestDbContextFactory.Create(Guid.NewGuid(), dbName))
        {
            afterFirst = (await check1.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId)).MfaSecret!;
        }

        // Second run: the row is already protected, so it must be left BYTE-IDENTICAL (never re-wrapped).
        using (var run2 = TestDbContextFactory.Create(Guid.NewGuid(), dbName))
        {
            await DbInitializer.BackfillLegacyMfaSecretsAsync(run2, protector, NullLogger.Instance, CancellationToken.None);
        }

        using var check2 = TestDbContextFactory.Create(Guid.NewGuid(), dbName);
        var afterSecond = (await check2.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId)).MfaSecret!;
        afterSecond.Should().Be(afterFirst, "an already-protected row must not be re-wrapped on the second run");
        protector.Unprotect(afterSecond).Should().Be(legacySecret,
            "Unprotect must still yield the ORIGINAL plaintext — proof there was no double-wrapping");
    }

    // -------- TC-PLT-005-07: a user with a null MfaSecret is left untouched --------
    [Fact]
    public async Task Backfill_LeavesNullMfaSecretUntouched_PLT005()
    {
        var dbName = Guid.NewGuid().ToString();
        var protector = new MfaSecretProtector(DataProtectionProvider.Create("tests"));
        var userId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(Guid.NewGuid(), dbName))
        {
            seed.Users.Add(new User { Id = userId, Email = "nomfa@acme.com", MfaEnabled = false, MfaSecret = null });
            await seed.SaveChangesAsync();
        }

        using (var run = TestDbContextFactory.Create(Guid.NewGuid(), dbName))
        {
            await DbInitializer.BackfillLegacyMfaSecretsAsync(run, protector, NullLogger.Instance, CancellationToken.None);
        }

        using var check = TestDbContextFactory.Create(Guid.NewGuid(), dbName);
        (await check.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId)).MfaSecret
            .Should().BeNull("a user with no MFA secret must not be given one");
    }

    private static AuthService CreateAuthService(
        AppDbContext db,
        ITenantContext tenantContext,
        ITotpService totpService,
        IFieldProtector protector)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "hrm-api-test",
                ["Jwt:Audience"] = "hrm-client-test",
                ["Platform:BaseDomain"] = "yourhrm.test",
            })
            .Build();

        return new AuthService(
            db,
            new JwtService(configuration),
            tenantContext,
            totpService,
            configuration,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Substitute.For<ILogger<AuthService>>(),
            Substitute.For<Hangfire.IBackgroundJobClient>(),
            currentUser: null,
            mfaSecretProtector: protector);
    }
}
