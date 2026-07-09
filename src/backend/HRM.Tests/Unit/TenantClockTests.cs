// ============================================================================
// ISSUE-065 / Phase 2b regression tests for TenantClock (branch fix/phase2b-timezone).
//
// TenantClock is the load-bearing PURE seam of the tenant-timezone work: attendance
// instants are always STORED in UTC, and only the DERIVED values (which calendar day a
// punch belongs to, the wall-clock time-of-day used for late/early comparison, and the
// "today" used for lookback/lock checks) are projected into the tenant's local zone.
//
// These tests pin two properties with exact, DST-aware assertions against a real
// TimeZoneInfo (America/New_York) — no mocks:
//   1. THE SAFETY PROPERTY: for TimeZoneInfo.Utc every conversion is a byte-identical
//      no-op, so UTC-default tenants (and the entire pre-existing attendance suite) are
//      unchanged. If this property breaks, the "no existing attendance test changes"
//      claim is false.
//   2. THE FIX: for a non-UTC zone the day-boundary and wall-clock cross correctly, which
//      is what makes ISSUE-065 (late/early keyed on LOCAL time, not UTC) work.
//
// Traceability: @TC-ATT-TZ-001 (UTC no-op) · @TC-ATT-TZ-002 (non-UTC conversion) ·
// @TC-ATT-TZ-003 (invalid/blank zone → UTC fallback, never throws) · @TC-ATT-TZ-004
// (LocalToUtc round-trip) · @TC-ATT-TZ-005 (ISSUE-251: DST spring-forward gap snaps forward,
// no longer throws; UTC path byte-identical).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Helpers;

namespace HRM.Tests.Unit;

public sealed class TenantClockTests
{
    // America/New_York is UTC-5 in winter (EST) and UTC-4 in summer (EDT) — a zone with a
    // real, DST-aware offset, so a UTC-only implementation would give visibly wrong answers.
    private static readonly TimeZoneInfo NewYork = TenantClock.ResolveTimeZone("America/New_York");

    // ── 1. UTC no-op — the safety property (@TC-ATT-TZ-001) ─────────────────────

    [Fact]
    public void ResolveTimeZone_Utc_ReturnsUtc()
    {
        TenantClock.ResolveTimeZone("UTC").Should().Be(TimeZoneInfo.Utc);
    }

    [Fact]
    public void LocalDateOf_UtcZone_EqualsPlainDateOfInstant()
    {
        // For a UTC zone the local day is exactly DateOnly.FromDateTime(instant) — the prior behavior.
        var instant = new DateTime(2026, 1, 15, 2, 30, 0, DateTimeKind.Utc);

        TenantClock.LocalDateOf(instant, TimeZoneInfo.Utc)
            .Should().Be(DateOnly.FromDateTime(instant))
            .And.Be(new DateOnly(2026, 1, 15));
    }

    [Fact]
    public void LocalTimeOfDay_UtcZone_EqualsPlainTimeOfInstant()
    {
        var instant = new DateTime(2026, 1, 15, 2, 30, 0, DateTimeKind.Utc);

        TenantClock.LocalTimeOfDay(instant, TimeZoneInfo.Utc)
            .Should().Be(TimeOnly.FromDateTime(instant))
            .And.Be(new TimeOnly(2, 30));
    }

    [Fact]
    public void LocalToUtc_UtcZone_EqualsDateToDateTimeUtc()
    {
        var date = new DateOnly(2026, 1, 15);
        var time = new TimeOnly(9, 0);

        var result = TenantClock.LocalToUtc(date, time, TimeZoneInfo.Utc);

        // Byte-identical to the prior date.ToDateTime(time, DateTimeKind.Utc) behavior.
        result.Should().Be(new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc));
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void TodayIn_UtcZone_EqualsUtcNowDate()
    {
        // Bracket the call so a run that straddles UTC midnight is not flaky.
        var before = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = TenantClock.TodayIn(TimeZoneInfo.Utc);
        var after = DateOnly.FromDateTime(DateTime.UtcNow);

        result.Should().BeOneOf(before, after);
    }

    // ── 2. Non-UTC conversion — the fix (@TC-ATT-TZ-002) ────────────────────────

    [Fact]
    public void ResolveTimeZone_NewYork_IsNotUtc()
    {
        // Proves .NET 10 (ICU) resolves the IANA id on this host — the whole feature depends on it.
        NewYork.Should().NotBe(TimeZoneInfo.Utc);
    }

    [Fact]
    public void LocalDateOf_NewYork_CrossesToPreviousLocalDay()
    {
        // 02:30Z is still the PREVIOUS local day in New York winter (UTC-5 → 21:30 the day before).
        var instant = new DateTime(2026, 1, 15, 2, 30, 0, DateTimeKind.Utc);

        TenantClock.LocalDateOf(instant, NewYork).Should().Be(new DateOnly(2026, 1, 14));
    }

    [Fact]
    public void LocalTimeOfDay_NewYork_ShiftsWallClockByOffset()
    {
        // Same 02:30Z instant → 21:30 local (UTC-5). The wall-clock crosses correctly.
        var instant = new DateTime(2026, 1, 15, 2, 30, 0, DateTimeKind.Utc);

        TenantClock.LocalTimeOfDay(instant, NewYork).Should().Be(new TimeOnly(21, 30));
    }

    [Fact]
    public void LocalTimeOfDay_NewYork_MorningInstant_IsLocalMorning()
    {
        // 14:30Z in winter → 09:30 New York local — the instant behind the ISSUE-065 late test.
        var instant = new DateTime(2026, 1, 15, 14, 30, 0, DateTimeKind.Utc);

        TenantClock.LocalDateOf(instant, NewYork).Should().Be(new DateOnly(2026, 1, 15));
        TenantClock.LocalTimeOfDay(instant, NewYork).Should().Be(new TimeOnly(9, 30));
    }

    // ── 3. Invalid / blank id → UTC, never throws (@TC-ATT-TZ-003) ──────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not/AZone")]
    public void ResolveTimeZone_InvalidOrBlank_FallsBackToUtcWithoutThrowing(string? id)
    {
        TimeZoneInfo? resolved = null;
        var act = () => resolved = TenantClock.ResolveTimeZone(id);

        act.Should().NotThrow();
        resolved.Should().Be(TimeZoneInfo.Utc);
    }

    // ── 4. LocalToUtc round-trip for a non-UTC wall-clock (@TC-ATT-TZ-004) ──────

    [Fact]
    public void LocalToUtc_NewYork_RoundTripsToCorrectUtcInstant()
    {
        var localDate = new DateOnly(2026, 1, 15);
        var localTime = new TimeOnly(9, 30);

        var utc = TenantClock.LocalToUtc(localDate, localTime, NewYork);

        // 09:30 New York winter (UTC-5) == 14:30Z.
        utc.Should().Be(new DateTime(2026, 1, 15, 14, 30, 0, DateTimeKind.Utc));

        // …and the projection back recovers the original local wall-clock + day.
        TenantClock.LocalDateOf(utc, NewYork).Should().Be(localDate);
        TenantClock.LocalTimeOfDay(utc, NewYork).Should().Be(localTime);
    }

    // ── 5. DST spring-forward gap — THE ISSUE-251 FIX (@TC-ATT-TZ-005) ──────────
    //
    // A wall-clock time inside the skipped spring-forward hour does not exist in the zone.
    // BEFORE the fix, LocalToUtc let ConvertTimeToUtc throw ArgumentException (a latent 500 on a
    // regularization submit for that once-a-year hour). AFTER the fix, the invalid time is snapped
    // forward past the gap to the first valid instant, then converted.
    //
    // Uses the Windows zone id "Pacific Standard Time" (the test host is Windows). On 2026-03-08 the
    // US Pacific zone springs forward 02:00 → 03:00, so 02:30 local does not exist; snapping +1h gives
    // 03:30 PDT (UTC-7) == 10:30 UTC. Deterministic (the transition date is fixed) → non-flaky.
    private static readonly TimeZoneInfo Pacific = ResolvePacific();

    private static TimeZoneInfo ResolvePacific()
    {
        // Prefer the Windows id (host is Windows); fall back to the IANA id if the runner only has ICU.
        try { return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"); }
    }

    [Fact]
    public void LocalToUtc_Pacific_SpringForwardGap_SnapsForward_DoesNotThrow_ISSUE251()
    {
        var gapDate = new DateOnly(2026, 3, 8);
        var gapTime = new TimeOnly(2, 30);   // inside the skipped 02:00–02:59 hour — invalid.

        DateTime utc = default;
        var act = () => utc = TenantClock.LocalToUtc(gapDate, gapTime, Pacific);

        act.Should().NotThrow();
        // Snapped to 03:30 PDT (UTC-7) → 10:30 UTC.
        utc.Should().Be(new DateTime(2026, 3, 8, 10, 30, 0, DateTimeKind.Utc));
        utc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void LocalToUtc_Pacific_ValidTime_ConvertsUnchanged()
    {
        // 05:00 on 2026-03-08 is AFTER the 03:00 spring-forward, so PDT (UTC-7) is in effect.
        var utc = TenantClock.LocalToUtc(new DateOnly(2026, 3, 8), new TimeOnly(5, 0), Pacific);

        utc.Should().Be(new DateTime(2026, 3, 8, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void LocalToUtc_UtcZone_GapDate_ByteIdenticalToPriorBehavior()
    {
        // The gap only exists in DST zones; for UTC the same date/time is an exact no-op (regression guard
        // that the ISSUE-251 branch never touches the UTC path).
        var date = new DateOnly(2026, 3, 8);
        var time = new TimeOnly(2, 30);

        TenantClock.LocalToUtc(date, time, TimeZoneInfo.Utc)
            .Should().Be(date.ToDateTime(time, DateTimeKind.Utc));
    }
}
