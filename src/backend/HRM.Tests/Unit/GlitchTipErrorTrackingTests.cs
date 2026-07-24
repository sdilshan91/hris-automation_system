// ============================================================================
// US-PLT-006: error tracking via self-hosted GlitchTip (Sentry-API-compatible).
//
// Two behaviours are exercised as pure functions (no SDK boot, deterministic):
//   (a) the BeforeSend PII scrub genuinely strips request body / Authorization header / cookies / query / email
//       / national-id from a sample event, while preserving benign data and the tenant tags (AC-2 / FR-4).
//   (b) the inert-guard: a blank/absent GlitchTip:Dsn ⇒ the wiring is disabled (no SDK init, no network) — the
//       single predicate that gates BOTH UseSentry and the Serilog sink (AC-3).
// ============================================================================

using FluentAssertions;
using HRM.Api.Observability;
using Microsoft.Extensions.Configuration;
using Sentry;

namespace HRM.Tests.Unit;

[Trait("TC", "TC-PLT-006")]
[Trait("Category", "ErrorTracking")]
public sealed class GlitchTipErrorTrackingTests
{
    // ---- (a) BeforeSend PII scrub (AC-2 / FR-4) --------------------------------------------------------------

    private static SentryEvent BuildEventWithPii()
    {
        var evt = new SentryEvent
        {
            Request = new SentryRequest
            {
                Method = "POST",
                Url = "https://acme.yourhrm.com/api/v1/employees",
                QueryString = "search=john.doe@example.com&national_id=901234567V",
                Cookies = "session=abc123; .AspNetCore.Session=deadbeef",
                Data = "{\"email\":\"john.doe@example.com\",\"nationalId\":\"901234567V\",\"salary\":50000}",
            },
            User = new SentryUser
            {
                Id = "user-guid",
                Email = "john.doe@example.com",
                IpAddress = "203.0.113.7",
            },
        };

        evt.Request.Headers["Authorization"] = "Bearer eyJhbGciOiJSUzI1Ni.super-secret-token";
        evt.Request.Headers["Cookie"] = "session=abc123"; // Sentry does NOT auto-scrub this — our scrubber must
        evt.Request.Headers["X-Api-Key"] = "key-abcdef";  // Sentry does NOT auto-scrub this — our scrubber must
        evt.Request.Headers["Accept"] = "application/json";
        evt.Request.Headers["User-Agent"] = "xunit";

        evt.SetExtra("email", "john.doe@example.com");
        evt.SetExtra("national_id", "901234567V");
        evt.SetExtra("order_id", "ORD-42"); // benign — must survive

        // A tenant tag as the event processor would have stamped it — must survive the scrub.
        evt.SetTag("tenant_id", "11111111-1111-1111-1111-111111111111");
        evt.SetTag("tenant_subdomain", "acme");

        return evt;
    }

    [Fact]
    public void Scrub_removes_request_body_query_and_cookies()
    {
        var evt = BuildEventWithPii();

        var result = SentryPiiScrubber.Scrub(evt);

        result.Should().BeSameAs(evt); // scrubs in place, still delivered
        result!.Request!.Data.Should().BeNull("the request body may contain PII (email, national id, salary)");
        result.Request.QueryString.Should().BeNull("query params can carry PII");
        result.Request.Cookies.Should().BeNull("cookies carry the session");
    }

    [Fact]
    public void Scrub_redacts_sensitive_headers_but_keeps_benign_headers()
    {
        var evt = BuildEventWithPii();

        SentryPiiScrubber.Scrub(evt);

        // Cookie + X-Api-Key are NOT auto-scrubbed by the Sentry SDK (only Authorization is), so these prove OUR
        // scrubber actually ran — a no-op mutant would leave the raw session/key here and fail the test.
        evt.Request!.Headers["Cookie"].Should().Be(SentryPiiScrubber.Redacted);
        evt.Request.Headers["X-Api-Key"].Should().Be(SentryPiiScrubber.Redacted);
        evt.Request.Headers["Cookie"].Should().NotContain("abc123");
        // Authorization is redacted too (belt-and-braces: the Sentry SDK also auto-scrubs it).
        evt.Request.Headers["Authorization"].Should().Be(SentryPiiScrubber.Redacted);
        evt.Request.Headers["Authorization"].Should().NotContain("super-secret-token");
        // Benign headers are untouched.
        evt.Request.Headers["Accept"].Should().Be("application/json");
        evt.Request.Headers["User-Agent"].Should().Be("xunit");
    }

    [Fact]
    public void Scrub_removes_user_email_and_ip()
    {
        var evt = BuildEventWithPii();

        SentryPiiScrubber.Scrub(evt);

        evt.User!.Email.Should().BeNull();
        evt.User.IpAddress.Should().BeNull();
        evt.User.Id.Should().Be("user-guid"); // opaque id is not PII — kept
    }

    [Fact]
    public void Scrub_redacts_email_and_national_id_extra_fields_but_keeps_benign_extra()
    {
        var evt = BuildEventWithPii();

        SentryPiiScrubber.Scrub(evt);

        evt.Extra["email"].Should().Be(SentryPiiScrubber.Redacted);
        evt.Extra["national_id"].Should().Be(SentryPiiScrubber.Redacted);
        evt.Extra["order_id"].Should().Be("ORD-42"); // benign field survives
    }

    [Fact]
    public void Scrub_preserves_tenant_tags()
    {
        var evt = BuildEventWithPii();

        SentryPiiScrubber.Scrub(evt);

        // Per-tenant triage tags are NOT PII and must survive the scrub (NFR-2 / BR-5).
        evt.Tags["tenant_id"].Should().Be("11111111-1111-1111-1111-111111111111");
        evt.Tags["tenant_subdomain"].Should().Be("acme");
    }

    [Fact]
    public void Scrub_is_null_safe_on_empty_event()
    {
        SentryPiiScrubber.Scrub(null).Should().BeNull();

        var bare = new SentryEvent(); // no Request, no User, no Extra
        var act = () => SentryPiiScrubber.Scrub(bare);
        act.Should().NotThrow();
    }

    // ---- (b) inert-guard: blank DSN ⇒ wiring disabled (AC-3) --------------------------------------------------

    private static IConfiguration Config(string? dsn)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [GlitchTipErrorTracking.DsnKey] = dsn,
            })
            .Build();

    [Theory]
    [InlineData(null)]   // key absent
    [InlineData("")]     // blank (the shipped default)
    [InlineData("   ")]  // whitespace
    public void IsEnabled_is_false_when_dsn_is_blank_or_absent(string? dsn)
    {
        // AC-3: a blank/absent DSN keeps the whole integration inert — this predicate gates both UseSentry
        // and the Serilog sink, so false here means no SDK init and no network calls.
        GlitchTipErrorTracking.IsEnabled(Config(dsn)).Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_is_true_when_dsn_is_configured()
    {
        GlitchTipErrorTracking.IsEnabled(Config("https://public@glitchtip.internal/1")).Should().BeTrue();
    }
}
