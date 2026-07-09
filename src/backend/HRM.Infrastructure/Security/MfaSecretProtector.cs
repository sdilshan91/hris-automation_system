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
    {
        try
        {
            return _protector.Unprotect(storedValue);
        }
        catch (CryptographicException)
        {
            // Legacy plaintext (or protected by a now-rotated key): use as-is so a valid MFA check still passes.
            return storedValue;
        }
        catch (FormatException)
        {
            // Legacy plaintext that is not valid base64url — Data Protection rejects it before decrypting.
            return storedValue;
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
}
