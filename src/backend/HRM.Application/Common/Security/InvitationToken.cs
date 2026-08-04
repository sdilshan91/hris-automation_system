using System.Security.Cryptography;
using System.Text;

namespace HRM.Application.Common.Security;

/// <summary>
/// The one-time token carried by a tenant user-invitation email (US-ADM-005), and the ONLY place its
/// transformation is defined.
///
/// <para><b>Why this class exists (BUG-294).</b> The mint side lived as a private static in
/// <c>UserManagementService</c> and there was no verify side at all — the invitation token was written and
/// rotated but never checked, so an invited user could never redeem their invitation. Adding a verifier meant
/// re-expressing the transformation somewhere else, and a second copy that drifts is exactly how a token stops
/// validating. Mint and verify now share one expression.</para>
///
/// <para><b>The trap this closes.</b> <see cref="Convert.ToHexString(byte[])"/> returns <b>UPPERCASE</b> hex.
/// The functionally identical reset-token hasher in <c>AuthService</c> appends
/// <c>ToLowerInvariant()</c>. Copying that helper here would have produced lowercase hex that never matches a
/// stored invitation hash — a mismatch that surfaces as neither a compile error nor a test failure, only as
/// "the link never works". <see cref="Verify"/> therefore compares case-insensitively, so it is correct
/// against rows written by either convention.</para>
/// </summary>
public static class InvitationToken
{
    /// <summary>Bytes of entropy behind each token. 256 bits — the same budget as the reset token.</summary>
    private const int TokenBytes = 32;

    /// <summary>
    /// Mints a new token: the RAW value (emailed to the invitee, never stored, never logged) and the hash
    /// (stored on the invitation row). Only the raw value can be presented; only the hash is persisted.
    /// </summary>
    public static (string RawToken, string TokenHash) Generate()
    {
        Span<byte> bytes = stackalloc byte[TokenBytes];
        RandomNumberGenerator.Fill(bytes);
        var raw = Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (raw, Hash(raw));
    }

    /// <summary>
    /// The stored form of a raw token. Hashes the UTF-8 bytes of the base64url STRING (not the underlying
    /// random bytes) — preserved exactly from the original mint so tokens issued before BUG-294 still verify.
    /// </summary>
    public static string Hash(string rawToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    /// <summary>
    /// Constant-time check of a presented raw token against a stored hash.
    ///
    /// <para>Comparison is over the upper-cased hex of both sides: the digests are fixed-length so no length
    /// information leaks, and normalising case first means a hash stored by either the uppercase (invitation)
    /// or lowercase (reset) convention verifies correctly. Returns false for a null/empty input rather than
    /// throwing — an absent token is a failed verification, not an error.</para>
    /// </summary>
    public static bool Verify(string? rawToken, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || string.IsNullOrWhiteSpace(storedHash))
            return false;

        var presented = Encoding.UTF8.GetBytes(Hash(rawToken));
        var stored = Encoding.UTF8.GetBytes(storedHash.ToUpperInvariant());

        return CryptographicOperations.FixedTimeEquals(presented, stored);
    }
}
