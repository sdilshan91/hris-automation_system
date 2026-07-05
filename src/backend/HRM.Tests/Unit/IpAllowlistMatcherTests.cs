// ============================================================================
// US-ATT-004 / ISSUE-066: attendance IP allowlist must match CIDR ranges, not
// only exact IPs. Regression tests for the IpAllowlistMatcher helper introduced
// by the fix (System.Net.IPNetwork-based containment).
//
// PRE-FIX BEHAVIOUR (HEAD): AttendanceService gated clock-in with
//   !settings.IpAllowlist.Contains(ip)   // exact string equality only
// so a CIDR entry such as "10.0.0.0/24" could never match a *distinct* in-range
// client IP like "10.0.0.5" — an in-range clock-in was wrongly denied (403).
//
// POST-FIX BEHAVIOUR (under test): IpAllowlistMatcher.IsAllowed treats each entry
// as either an exact IP OR a CIDR range; a malformed entry is skipped (never
// throws). These are pure unit tests on the helper (no DB / no HTTP).
//
// PROVIDER: none — pure static helper, mirrors the calc-helper unit tests here.
// ============================================================================

using FluentAssertions;
using HRM.Infrastructure.Services;

namespace HRM.Tests.Unit;

public sealed class IpAllowlistMatcherTests
{
    // ── ISSUE-066 core: a client IP inside a CIDR entry matches; outside doesn't ──

    [Fact]
    public void IpAllowlist_CidrRange_Matches_ISSUE066()
    {
        var allowlist = new[] { "10.0.0.0/24" };

        // 10.0.0.5 is inside 10.0.0.0/24 — the exact-match pre-fix code denied this.
        IpAllowlistMatcher.IsAllowed("10.0.0.5", allowlist).Should().BeTrue(
            "10.0.0.5 falls within the 10.0.0.0/24 range");

        // 10.0.1.5 is outside the /24 — must still be denied.
        IpAllowlistMatcher.IsAllowed("10.0.1.5", allowlist).Should().BeFalse(
            "10.0.1.5 is outside the 10.0.0.0/24 range");
    }

    [Fact]
    public void IpAllowlist_CidrRange_BoundaryHosts_Match()
    {
        var allowlist = new[] { "192.168.1.0/28" }; // hosts .0 – .15

        IpAllowlistMatcher.IsAllowed("192.168.1.0", allowlist).Should().BeTrue();
        IpAllowlistMatcher.IsAllowed("192.168.1.15", allowlist).Should().BeTrue();
        IpAllowlistMatcher.IsAllowed("192.168.1.16", allowlist).Should().BeFalse();
    }

    // ── Regression guard: an exact-IP entry still matches (unchanged pre/post) ──

    [Fact]
    public void IpAllowlist_ExactEntry_StillMatches_ISSUE066()
    {
        var allowlist = new[] { "198.51.100.7" };

        IpAllowlistMatcher.IsAllowed("198.51.100.7", allowlist).Should().BeTrue();
        IpAllowlistMatcher.IsAllowed("198.51.100.8", allowlist).Should().BeFalse();
    }

    [Fact]
    public void IpAllowlist_MixedExactAndCidrEntries_BothMatch()
    {
        var allowlist = new[] { "198.51.100.7", "10.0.0.0/24" };

        IpAllowlistMatcher.IsAllowed("198.51.100.7", allowlist).Should().BeTrue(); // exact hit
        IpAllowlistMatcher.IsAllowed("10.0.0.42", allowlist).Should().BeTrue();    // CIDR hit
        IpAllowlistMatcher.IsAllowed("203.0.113.9", allowlist).Should().BeFalse(); // neither
    }

    // ── A malformed entry must be skipped, never crash enforcement ──

    [Fact]
    public void IpAllowlist_MalformedEntry_IsSkipped_NoCrash_ISSUE066()
    {
        // "not-an-ip" and "10.0.0.0/999" are junk; the valid CIDR after them must still match,
        // and a non-matching IP must return false rather than throw.
        var allowlist = new[] { "not-an-ip", "10.0.0.0/999", "10.0.0.0/24" };

        Action inRange = () => IpAllowlistMatcher.IsAllowed("10.0.0.5", allowlist);
        inRange.Should().NotThrow();
        IpAllowlistMatcher.IsAllowed("10.0.0.5", allowlist).Should().BeTrue(
            "the trailing valid CIDR entry still matches despite the malformed entries");

        IpAllowlistMatcher.IsAllowed("172.16.0.1", new[] { "not-an-ip" }).Should().BeFalse(
            "a single malformed entry matches nothing and does not throw");
    }

    // ── Empty / missing client IP denies (preserves the pre-CIDR deny default) ──

    [Fact]
    public void IpAllowlist_MissingOrUnparseableClientIp_Denies()
    {
        var allowlist = new[] { "10.0.0.0/24" };

        IpAllowlistMatcher.IsAllowed(null, allowlist).Should().BeFalse();
        IpAllowlistMatcher.IsAllowed("", allowlist).Should().BeFalse();
        IpAllowlistMatcher.IsAllowed("   ", allowlist).Should().BeFalse();
        IpAllowlistMatcher.IsAllowed("garbage", allowlist).Should().BeFalse();
    }

    [Fact]
    public void IpAllowlist_EmptyAllowlist_Denies()
    {
        IpAllowlistMatcher.IsAllowed("10.0.0.5", Array.Empty<string>()).Should().BeFalse();
    }
}
