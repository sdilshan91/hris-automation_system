using System.Security.Cryptography;
using System.Text;

namespace HRM.Domain.Recruitment;

/// <summary>
/// Pure, dependency-free magic-link token codec for the candidate portal (US-REC-008 FR-1/NFR-4). A token
/// is <c>{payload}.{signature}</c> where:
/// <list type="bullet">
///   <item>payload = base64url("{tenantId}|{emailLower}|{expiryUnixSeconds}")</item>
///   <item>signature = base64url(HMAC-SHA256(payload, serverSecret))</item>
/// </list>
/// The server secret is supplied by the caller (read from configuration — Jwt:PrivateKey or a dedicated
/// key; never hardcoded). Verification recomputes the HMAC in constant time and re-parses the payload.
/// This class does NOT touch the database — the persisted hash + expiry row are handled by the service.
/// Kept pure so it can be unit-tested in isolation (sign/verify roundtrip, expiry, tampering).
/// </summary>
public static class PortalMagicLink
{
    private const char Separator = '.';
    private const char FieldSeparator = '|';

    /// <summary>
    /// Builds a signed token binding the tenant + (lower-cased) email + expiry, signed with the secret.
    /// </summary>
    public static string Issue(Guid tenantId, string email, DateTime expiresAtUtc, string secret)
    {
        var emailLower = (email ?? string.Empty).Trim().ToLowerInvariant();
        var expiryUnix = new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var rawPayload = $"{tenantId:D}{FieldSeparator}{emailLower}{FieldSeparator}{expiryUnix}";

        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(rawPayload));
        var signature = Base64UrlEncode(ComputeHmac(payload, secret));

        return $"{payload}{Separator}{signature}";
    }

    /// <summary>
    /// Verifies a token's signature and parses its payload. Returns true with the bound tenant/email/expiry
    /// on success; false (with default outputs) if the token is malformed, the signature does not match, or
    /// the payload cannot be parsed. Expiry itself is NOT enforced here — the caller compares
    /// <paramref name="expiresAtUtc"/> against the current time (so the service can log/branch precisely).
    /// </summary>
    public static bool TryVerify(
        string? token, string secret,
        out Guid tenantId, out string email, out DateTime expiresAtUtc)
    {
        tenantId = Guid.Empty;
        email = string.Empty;
        expiresAtUtc = default;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        var parts = token.Split(Separator);
        if (parts.Length != 2)
            return false;

        var payload = parts[0];
        var presentedSig = parts[1];

        byte[] expectedSig;
        byte[] presentedSigBytes;
        try
        {
            expectedSig = ComputeHmac(payload, secret);
            presentedSigBytes = Base64UrlDecode(presentedSig);
        }
        catch
        {
            return false;
        }

        // Constant-time comparison defeats timing attacks (NFR-4).
        if (!CryptographicOperations.FixedTimeEquals(expectedSig, presentedSigBytes))
            return false;

        string rawPayload;
        try
        {
            rawPayload = Encoding.UTF8.GetString(Base64UrlDecode(payload));
        }
        catch
        {
            return false;
        }

        var fields = rawPayload.Split(FieldSeparator);
        if (fields.Length != 3)
            return false;

        if (!Guid.TryParse(fields[0], out tenantId))
            return false;

        email = fields[1];

        if (!long.TryParse(fields[2], out var expiryUnix))
            return false;

        expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expiryUnix).UtcDateTime;
        return true;
    }

    /// <summary>
    /// SHA-256 hex digest of the full token string — this is what is persisted (the raw token is never
    /// stored, NFR-4). Lookup re-hashes the presented token and matches this value.
    /// </summary>
    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static byte[] ComputeHmac(string payload, string secret)
    {
        if (string.IsNullOrEmpty(secret))
            throw new InvalidOperationException("The portal magic-link signing secret is not configured.");

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
