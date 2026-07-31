// ============================================================================
// ISSUE-336 — a lost Data Protection key ring must be OBSERVABLE, not silent.
//
// MfaSecretProtector.Unprotect deliberately falls back to returning the stored value as-is, so pre-encryption
// legacy plaintext still validates. That fallback is correct, but it also means a value that IS encrypted and
// CANNOT be decrypted (key ring lost or re-keyed) is handed to TOTP validation as CIPHERTEXT — which can never
// match a code. The user sees "invalid code" forever, with nothing anywhere explaining why.
//
// It is not a hard lockout: recovery codes are hashed and verified independently of this key, so the user can
// still get in and re-enrol. That is why this is LOW rather than HIGH. But if the key ring were lost for the
// whole fleet, the only symptom would be a rising tide of "my authenticator stopped working" tickets with no
// diagnostic thread to pull.
//
// The distinction that makes detection possible already existed in the catch filter:
//   FormatException        → not valid base64url → genuine legacy plaintext → benign, stays quiet
//   CryptographicException → Data-Protection-shaped but undecryptable      → the dangerous case → WARN
// ============================================================================

using System.Security.Cryptography;
using FluentAssertions;
using HRM.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace HRM.Tests.Unit;

public sealed class MfaSecretProtectorKeyLossTests
{
    // Binds @TC-AUTH-336-01.
    [Fact]
    public void Undecryptable_protected_value_logs_a_warning_ISSUE336()
    {
        // Protect under ring 1, then attempt to read it under a DIFFERENT ring — exactly what a lost or
        // re-keyed key ring looks like to the application.
        var ringOne = new MfaSecretProtector(new EphemeralDataProtectionProvider());
        var protectedSecret = ringOne.Protect("JBSWY3DPEHPK3PXP");

        var log = new CapturingLogger();
        var ringTwo = new MfaSecretProtector(new EphemeralDataProtectionProvider(), log);

        var result = ringTwo.Unprotect(protectedSecret);

        result.Should().Be(protectedSecret,
            "the documented fallback returns the stored value as-is — this arm pins the OBSERVABILITY, not a "
            + "behaviour change; altering the fallback would break legacy-plaintext support");

        log.Warnings.Should().ContainSingle(
            "a lost/re-keyed ring is the operationally dangerous case and must be reported exactly once");
        log.Warnings[0].Should().Contain("key ring");
    }

    // Binds @TC-AUTH-336-02. THE discriminating arm: genuine legacy plaintext must stay SILENT, or the warning
    // fires for every pre-encryption secret still in the database and becomes noise nobody reads.
    [Fact]
    public void Legacy_plaintext_does_NOT_warn_ISSUE336()
    {
        var log = new CapturingLogger();
        var protector = new MfaSecretProtector(new EphemeralDataProtectionProvider(), log);

        // A raw base32 TOTP secret. It is not base64url and carries no Data Protection magic header, so the
        // shape check classifies it as never-encrypted. (It still throws CryptographicException internally —
        // which is exactly why exception type cannot be the discriminator.)
        var result = protector.Unprotect("JBSWY3DPEHPK3PXP");

        result.Should().Be("JBSWY3DPEHPK3PXP", "legacy plaintext must still validate");
        log.Warnings.Should().BeEmpty(
            "legacy plaintext is expected and benign; warning on it would drown the real signal in noise from "
            + "every pre-encryption secret in the table");
    }

    // Binds @TC-AUTH-336-03. A round-trip under the SAME ring is the healthy path and must be silent.
    [Fact]
    public void Healthy_round_trip_does_NOT_warn_ISSUE336()
    {
        var log = new CapturingLogger();
        var provider = new EphemeralDataProtectionProvider();
        var protector = new MfaSecretProtector(provider, log);

        var stored = protector.Protect("JBSWY3DPEHPK3PXP");
        protector.Unprotect(stored).Should().Be("JBSWY3DPEHPK3PXP");

        log.Warnings.Should().BeEmpty("the healthy path must never warn");
    }

    // Binds @TC-AUTH-336-04. The warning must never carry secret material — it lands in a log file that is read
    // by QA and shipped to an aggregator.
    [Fact]
    public void Warning_never_contains_secret_material_ISSUE336()
    {
        const string secret = "JBSWY3DPEHPK3PXP";
        var protectedSecret = new MfaSecretProtector(new EphemeralDataProtectionProvider()).Protect(secret);

        var log = new CapturingLogger();
        new MfaSecretProtector(new EphemeralDataProtectionProvider(), log).Unprotect(protectedSecret);

        log.Warnings.Should().ContainSingle();
        log.Warnings[0].Should().NotContain(secret).And.NotContain(protectedSecret,
            "neither the plaintext nor the ciphertext may reach a log sink");
    }

    /// <summary>Minimal capturing logger — records rendered warning messages only, which is all these arms assert.</summary>
    private sealed class CapturingLogger : ILogger<MfaSecretProtector>
    {
        public List<string> Warnings { get; } = [];

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }

        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

}
