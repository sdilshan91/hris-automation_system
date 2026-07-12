using System.Text.Json;
using System.Text.Json.Nodes;

namespace HRM.Application.Features.AuditLog;

/// <summary>
/// US-ADM-008 (FR-4): pure, side-effect-free masker that, given a JSON string, replaces the VALUES of any
/// configured sensitive keys with <see cref="Redacted"/>, recursively through nested objects and arrays. The
/// match is case-insensitive and covers common naming variants (snake_case, camelCase, PascalCase) of each
/// configured key, so <c>password_hash</c>, <c>passwordHash</c> and <c>PasswordHash</c> are all caught.
///
/// <para>Applied at TWO points: (1) WRITE time as the primary line of defence — <c>AuditCaptureInterceptor</c>
/// masks the serialized <c>before</c>/<c>after</c> before they are persisted, so sensitive values never land in
/// the audit JSONB in cleartext (BUG-281, ahead of BUG-082 broadening auto-audit to PII-bearing entities); and
/// (2) READ time as defence-in-depth on the audit list summary, detail view, AND export (BR-4). <c>Mask</c> is
/// pure and idempotent, so re-masking an already-redacted row on read is safe. The visual diff (FR-3) is computed
/// on the FRONTEND from the masked before/after; the backend does not diff.</para>
/// </summary>
public static class SensitiveFieldMasker
{
    /// <summary>The literal placeholder substituted for any sensitive value.</summary>
    public const string Redacted = "***REDACTED***";

    /// <summary>
    /// The canonical sensitive keys (FR-4). Matching is done on a NORMALIZED form (lower-cased, with all
    /// non-alphanumeric characters stripped) so every casing/separator variant of these maps to one entry —
    /// e.g. "national_id", "nationalId", "National ID" all normalize to "nationalid".
    /// </summary>
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.Ordinal)
    {
        Normalize("password_hash"),
        Normalize("password"),
        Normalize("mfa_secret"),
        Normalize("bank_account_number"),
        Normalize("national_id"),
        // BUG-281: at-rest coverage for BUG-082's Employee/compensation entities — when auto-audit is
        // broadened to PII-bearing entities, any "salary"-named field value must be redacted before it is
        // persisted to the audit before/after JSONB (write-time), not just masked on the read path.
        Normalize("salary"),
    };

    /// <summary>
    /// Returns <paramref name="json"/> with every sensitive value redacted. Returns the input unchanged when it
    /// is null/whitespace or not valid JSON (a non-JSON detail string carries no structured fields to mask).
    /// </summary>
    public static string? Mask(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            // Not JSON (e.g. a plain Detail message) — nothing structured to mask.
            return json;
        }

        if (node is null)
            return json;

        MaskNode(node);
        return node.ToJsonString();
    }

    private static void MaskNode(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                // Snapshot keys first — we replace values in place while iterating.
                foreach (var key in obj.Select(kvp => kvp.Key).ToList())
                {
                    if (IsSensitive(key))
                    {
                        obj[key] = JsonValue.Create(Redacted);
                        continue;
                    }

                    var child = obj[key];
                    if (child is not null)
                        MaskNode(child);
                }
                break;

            case JsonArray arr:
                foreach (var item in arr)
                {
                    if (item is not null)
                        MaskNode(item);
                }
                break;
        }
    }

    private static bool IsSensitive(string key) => SensitiveKeys.Contains(Normalize(key));

    /// <summary>Lower-case and strip every non-alphanumeric char so casing/separator variants collapse to one.</summary>
    private static string Normalize(string key)
    {
        Span<char> buffer = key.Length <= 128 ? stackalloc char[key.Length] : new char[key.Length];
        var len = 0;
        foreach (var c in key)
        {
            if (char.IsLetterOrDigit(c))
                buffer[len++] = char.ToLowerInvariant(c);
        }
        return new string(buffer[..len]);
    }
}
