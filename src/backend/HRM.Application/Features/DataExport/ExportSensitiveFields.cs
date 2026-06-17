namespace HRM.Application.Features.DataExport;

/// <summary>
/// US-ADM-010 (FR-8/BR-7 — SECURITY-CRITICAL): the auth-secret deny-list applied by the CSV/JSONL serializers.
/// Authentication secrets (password hashes, MFA secrets, refresh/token hashes) are NEVER written into any export
/// file. PII (national id, bank account) IS exported (FR-8) — only auth secrets are stripped, so this is a
/// NARROWER list than the audit-log <c>SensitiveFieldMasker</c> (which also masks PII).
///
/// <para>Matching is on a NORMALIZED property name (lower-cased, non-alphanumerics stripped) using SUBSTRING
/// containment, so any property whose name CONTAINS a denied token is excluded — e.g. "PasswordHash",
/// "password_hash", "MfaSecret", "TotpSecret", "RefreshTokenHash", "TokenHash" all match. This is deliberately
/// broad: a false-positive (dropping a harmless column) is acceptable; leaking a credential is not.</para>
/// </summary>
public static class ExportSensitiveFields
{
    /// <summary>Normalized substrings that, if contained in a property name, exclude that property from exports.</summary>
    private static readonly string[] DeniedTokens =
    [
        "passwordhash",
        "password",
        "mfasecret",
        "totpsecret",
        "mfarecoverycode",
        "recoverycode",
        "tokenhash",
        "refreshtoken",
        "securitystamp",
    ];

    /// <summary>True iff a property with this name must be excluded from every export file (auth secret).</summary>
    public static bool IsDenied(string propertyName)
    {
        var normalized = Normalize(propertyName);
        foreach (var token in DeniedTokens)
        {
            if (normalized.Contains(token, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string Normalize(string name)
    {
        Span<char> buffer = name.Length <= 128 ? stackalloc char[name.Length] : new char[name.Length];
        var len = 0;
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
                buffer[len++] = char.ToLowerInvariant(c);
        }
        return new string(buffer[..len]);
    }
}
