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
