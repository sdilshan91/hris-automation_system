namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Reversible protection for a sensitive field stored at rest (US-AUTH-005 NFR-2) — currently the TOTP
/// MFA secret. Backed by ASP.NET Core Data Protection so a raw DB read does not disclose the plaintext.
/// </summary>
/// <remarks>
/// <para><see cref="Unprotect"/> is tolerant of <b>legacy plaintext</b>: rows written before encryption was
/// introduced are stored unencrypted, so an implementation that cannot decrypt a value must return it
/// as-is rather than throw — a valid MFA verification must never fail because a row is still legacy.</para>
/// </remarks>
public interface IFieldProtector
{
    /// <summary>Encrypts <paramref name="plaintext"/> for storage. Never called with null/empty.</summary>
    string Protect(string plaintext);

    /// <summary>
    /// Decrypts a stored value. If the value is legacy plaintext (not producible by this protector) it is
    /// returned unchanged instead of throwing.
    /// </summary>
    string Unprotect(string storedValue);
}
