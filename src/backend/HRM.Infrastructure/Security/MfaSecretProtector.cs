using System.Security.Cryptography;
using HRM.Application.Common.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Security;

/// <summary>
/// <see cref="IFieldProtector"/> backed by ASP.NET Core Data Protection, purpose-scoped to the TOTP MFA
/// secret (US-AUTH-005 NFR-2). Built-in, no external infra; DataProtection works out-of-the-box in the test
/// host. Registered as a singleton (the underlying protector is thread-safe).
/// </summary>
/// <remarks>
/// <para>KEY PERSISTENCE (ISSUE-247): the Data Protection key ring is persisted to Postgres via EF
/// (<c>PersistKeysToDbContext&lt;AppDbContext&gt;</c> + a fixed <c>SetApplicationName("HRM")</c>, see
/// <c>DependencyInjection.AddInfrastructure</c>), so keys survive redeploys and are shared across instances
/// — encrypted MFA secrets stay decryptable. The legacy-plaintext fallback in <see cref="Unprotect"/> still
/// covers pre-encryption values and any values written before a deliberate key re-key.</para>
/// </remarks>
public sealed class MfaSecretProtector : IFieldProtector
{
    /// <summary>Purpose string for the Data Protection protector. Versioned so a future re-key can bump it.</summary>
    public const string Purpose = "HRM.MfaSecret.v1";

    private readonly IDataProtector _protector;
    private readonly ILogger<MfaSecretProtector>? _logger;

    public MfaSecretProtector(IDataProtectionProvider provider, ILogger<MfaSecretProtector>? logger = null)
    {
        _protector = provider.CreateProtector(Purpose);
        _logger = logger;
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string storedValue)
        // Legacy plaintext (or a value protected by a now-rotated key) cannot be decrypted: use it as-is so a
        // valid MFA check still passes rather than throwing.
        => TryUnprotect(storedValue, out var plaintext) ? plaintext! : storedValue;

    public bool IsProtected(string storedValue) => TryUnprotect(storedValue, out _);

    /// <summary>
    /// Single decrypt attempt shared by <see cref="Unprotect"/> and <see cref="IsProtected"/> so the two can
    /// NEVER disagree about what "legacy plaintext" means (a value is legacy iff Data Protection cannot decrypt
    /// it). The tolerated exception set lives HERE, in one place: <see cref="CryptographicException"/> covers a
    /// wrong/rotated key or a tampered payload; <see cref="FormatException"/> covers a value that is not valid
    /// base64url (raw legacy plaintext), which Data Protection rejects before it even attempts to decrypt.
    /// </summary>
    private bool TryUnprotect(string storedValue, out string? plaintext)
    {
        try
        {
            plaintext = _protector.Unprotect(storedValue);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // ISSUE-336 — make a LOST KEY RING observable instead of silent.
            //
            // Discriminated by PAYLOAD SHAPE, not by exception type. The pre-existing comment here claimed
            // FormatException meant legacy plaintext and CryptographicException meant a bad key — that is FALSE,
            // and a test written against it failed: Data Protection throws CryptographicException for BOTH a raw
            // base32 TOTP secret and a genuinely undecryptable payload. Keying the warning off the exception
            // would fire it for every pre-encryption secret in the table and bury the real signal.
            //
            // A real payload is base64url and starts with Data Protection's 0x09F0C9F0 magic header, so the
            // shape check separates "encrypted but unreadable" (dangerous) from "never encrypted" (benign).
            //
            // The second case is the operationally dangerous one. Unprotect falls back to returning the stored
            // value as-is, so TOTP validation then runs against CIPHERTEXT and can never succeed: the user simply
            // sees "invalid code" forever. They CAN still get in with a recovery code (those are hashed and
            // verified independently of this key), so it is not a hard lockout — but nothing anywhere reports the
            // cause. If the key ring were lost for the whole fleet, the only symptom would be a rising tide of
            // "my authenticator stopped working" tickets. One WARNING per occurrence turns that into a signal.
            //
            // Deliberately logs NO secret material — not the stored value, not the plaintext, not the purpose
            // payload. The fact of the failure is the whole message.
            if (LooksDataProtectionEncrypted(storedValue))
            {
                _logger?.LogWarning(
                    "An MFA secret is Data-Protection-encrypted but could not be decrypted with the current key "
                    + "ring (purpose {Purpose}). TOTP validation for this user will fail until they re-enrol; "
                    + "recovery codes still work. This usually means the Data Protection key ring was lost or "
                    + "re-keyed — check that keys are persisting to the database.", Purpose);
            }

            plaintext = null;
            return false;
        }
    }

    /// <summary>
    /// True when <paramref name="value"/> has the shape of an ASP.NET Core Data Protection payload: valid
    /// base64url whose first four bytes are the 0x09F0C9F0 magic header. Used ONLY to decide whether a failed
    /// decrypt is worth warning about (ISSUE-336) — never to gate decryption itself.
    /// </summary>
    private static bool LooksDataProtectionEncrypted(string value)
    {
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(value);
            return bytes.Length >= 4
                && bytes[0] == 0x09 && bytes[1] == 0xF0 && bytes[2] == 0xC9 && bytes[3] == 0xF0;
        }
        catch (FormatException)
        {
            return false; // not base64url at all ⇒ definitely legacy plaintext
        }
    }
}

/// <summary>
/// No-op <see cref="IFieldProtector"/> that returns values unchanged. Used as the fallback when no protector
/// is injected (isolated unit construction of <c>AuthService</c>), preserving pre-encryption behavior.
/// </summary>
public sealed class PlaintextFieldProtector : IFieldProtector
{
    public static readonly PlaintextFieldProtector Instance = new();

    public string Protect(string plaintext) => plaintext;

    public string Unprotect(string storedValue) => storedValue;

    /// <summary>Always false — this protector encrypts nothing, so no value it "produces" is distinguishable
    /// from plaintext; every stored value is, by its definition, legacy plaintext.</summary>
    public bool IsProtected(string storedValue) => false;
}
