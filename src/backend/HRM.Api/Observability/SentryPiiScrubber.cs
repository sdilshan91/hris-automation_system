using Sentry;

namespace HRM.Api.Observability;

/// <summary>
/// US-PLT-006 (AC-2 / FR-4 / NFR-1 / BR-2): the mandatory PII scrub applied to every GlitchTip/Sentry event in
/// the <c>BeforeSend</c> hook, <b>before it ever leaves the process</b>. Extracted as a pure static function so
/// the scrub is directly unit-testable without booting the SDK (per the story's "extract the scrub function and
/// test THAT" guidance).
///
/// <para>Strips: the <b>request body</b> (<c>Request.Data</c>), the <b>query string</b>, <b>cookies/session</b>,
/// <b>sensitive headers</b> (<c>Authorization</c>, <c>Cookie</c>, …), and <b>known PII fields</b> (email, national
/// id, …) from the user + extra/env bags. Combined with <c>SendDefaultPii = false</c> on the SDK options this is
/// the "hard condition" under which self-hosting was approved (ADR-2026-07-08 Decision 1, BR-2).</para>
///
/// <para>The <c>tenant_id</c> / <c>tenant_subdomain</c> tags (added by <see cref="TenantTagSentryEventProcessor"/>)
/// are deliberately <b>preserved</b> — they are the whole point of per-tenant triage (NFR-2); a tenant id/subdomain
/// is not regulated PII.</para>
/// </summary>
public static class SentryPiiScrubber
{
    /// <summary>Replacement written in place of a scrubbed value (Sentry's own convention).</summary>
    public const string Redacted = "[Filtered]";

    /// <summary>Header names whose value must never leave the process (matched case-insensitively).</summary>
    public static readonly string[] SensitiveHeaderNames =
    {
        "Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie", "X-Api-Key",
    };

    /// <summary>
    /// Substrings that mark a key/field name as carrying PII or a secret — matched case-insensitively against the
    /// keys of the user/extra/env bags. Covers the story's explicit fields (email, national id) plus obvious
    /// credential/secret carriers so a stray field never leaks.
    /// </summary>
    public static readonly string[] PiiKeyMarkers =
    {
        "email", "national", "nid", "ssn", "passport", "password", "secret", "token", "authorization", "cookie",
    };

    /// <summary>
    /// Scrubs a Sentry event in place and returns it (never returns null for a non-null input, so the event is
    /// still delivered — only its PII is removed). Null-safe on every optional sub-object.
    /// </summary>
    public static SentryEvent? Scrub(SentryEvent? evt)
    {
        if (evt is null)
            return null;

        ScrubRequest(evt.Request);
        ScrubUser(evt.User);

        // Extra is a read-only bag exposed via SetExtra(); redact PII-marked keys through the setter.
        if (evt.Extra is { Count: > 0 })
        {
            foreach (var key in evt.Extra.Keys.ToList())
            {
                if (IsPiiKey(key))
                    evt.SetExtra(key, Redacted);
            }
        }

        return evt;
    }

    private static void ScrubRequest(SentryRequest? request)
    {
        if (request is null)
            return;

        request.Data = null;         // FR-4: request body
        request.QueryString = null;  // FR-4: query parameters
        request.Cookies = null;      // FR-4: cookies / session

        RedactSensitiveHeaders(request.Headers);
        RedactPiiKeys(request.Env);   // e.g. HTTP_COOKIE / HTTP_AUTHORIZATION CGI-style vars
        RedactPiiKeys(request.Other);
    }

    private static void ScrubUser(SentryUser? user)
    {
        if (user is null)
            return;

        user.Email = null;      // FR-4: known PII field (email)
        user.IpAddress = null;  // defense-in-depth alongside SendDefaultPii=false
        RedactPiiKeys(user.Other);
    }

    private static void RedactSensitiveHeaders(IDictionary<string, string>? headers)
    {
        if (headers is not { Count: > 0 })
            return;

        foreach (var key in headers.Keys.ToList())
        {
            if (SensitiveHeaderNames.Any(h => string.Equals(h, key, StringComparison.OrdinalIgnoreCase)))
                headers[key] = Redacted;
        }
    }

    private static void RedactPiiKeys(IDictionary<string, string>? bag)
    {
        if (bag is not { Count: > 0 })
            return;

        foreach (var key in bag.Keys.ToList())
        {
            if (IsPiiKey(key))
                bag[key] = Redacted;
        }
    }

    private static bool IsPiiKey(string key)
        => !string.IsNullOrEmpty(key)
           && PiiKeyMarkers.Any(m => key.Contains(m, StringComparison.OrdinalIgnoreCase));
}
