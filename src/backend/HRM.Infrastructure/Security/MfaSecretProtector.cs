using System.Security.Cryptography;
using HRM.Application.Common.Interfaces;
using Microsoft.AspNetCore.DataProtection;

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

    public MfaSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
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
            plaintext = null;
            return false;
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
