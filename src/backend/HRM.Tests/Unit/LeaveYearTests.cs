// ============================================================================
// CAL-8 / ISSUE-305: LeaveYear — the ONE definition of a tenant's leave-year boundaries.
//
// The leave subsystem hardcoded calendar 1 Jan – 31 Dec in four places while Tenant.FiscalYearStartMonth
// existed, was settable in the org-profile UI, and was read by NOTHING. An Apr–Mar tenant got a wrong
// leave-year boundary, accrual window and carry-forward expiry.
//
// The `Calendar_` arms are the no-regression contract: month 1 (the default, and every tenant that never
// configured this) must return EXACTLY the calendar year the four sites used to hardcode.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Leave;
using HRM.Infrastructure.Services;

namespace HRM.Tests.Unit;

public sealed class LeaveYearTests
{
    // ══ BoundsFor — calendar (the no-regression contract) ══

    /// <summary>
    /// CONTROL: month 1 returns exactly `new DateOnly(y,1,1) .. new DateOnly(y,12,31)` — byte-identical to the
    /// literals the four sites hardcoded. Note the end is 31 Dec of the SAME year, which the generic
    /// "AddYears(1).AddDays(-1)" must produce without a special case.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData(2025)]
    [InlineData(2026)]
    [InlineData(2028)]   // a leap year
    public void Calendar_BoundsAreTheUnchangedCalendarYear(int year)
    {
        var (start, end) = LeaveYear.BoundsFor(year, fiscalYearStartMonth: 1);

        start.Should().Be(new DateOnly(year, 1, 1));
        end.Should().Be(new DateOnly(year, 12, 31), "the calendar year ends 31 Dec of the SAME year");
    }

    // ══ BoundsFor — fiscal ══

    /// <summary>
    /// TC-LV-264 step 1 (ISSUE-305): an April tenant's leave year 2026 is 2026-04-01 .. 2027-03-31 — it SPANS
    /// the calendar boundary, which is exactly what the hardcoded Jan–Dec could never express.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public void AprilTenant_LeaveYearSpansTheCalendarBoundary()
    {
        var (start, end) = LeaveYear.BoundsFor(2026, fiscalYearStartMonth: 4);

        start.Should().Be(new DateOnly(2026, 4, 1));
        end.Should().Be(new DateOnly(2027, 3, 31), "the day before the next year's 1 April");
    }

    /// <summary>Every start month produces a full, contiguous, non-overlapping year.</summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(12)]
    public void BoundsFor_AnyStartMonth_IsAFullContiguousYear(int month)
    {
        var (start, end) = LeaveYear.BoundsFor(2026, month);
        var (nextStart, _) = LeaveYear.BoundsFor(2027, month);

        start.Month.Should().Be(month);
        end.AddDays(1).Should().Be(nextStart, "consecutive leave years must abut exactly — no gap, no overlap");
        end.Should().BeAfter(start);
    }

    /// <summary>
    /// A February start crossing a leap year: 2027-02-01 .. 2028-01-31. The end must not be dragged onto 29 Feb
    /// by naive month arithmetic.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public void FebruaryStart_AcrossALeapYear_EndsOn31January()
    {
        var (start, end) = LeaveYear.BoundsFor(2027, fiscalYearStartMonth: 2);

        start.Should().Be(new DateOnly(2027, 2, 1));
        end.Should().Be(new DateOnly(2028, 1, 31));
    }

    /// <summary>
    /// A December start is the tightest edge: 2026-12-01 .. 2027-11-30. The year is labelled by the calendar
    /// year it STARTS in, so all but one of its months fall in the NEXT calendar year.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public void DecemberStart_RunsToTheFollowingNovember()
    {
        var (start, end) = LeaveYear.BoundsFor(2026, fiscalYearStartMonth: 12);

        start.Should().Be(new DateOnly(2026, 12, 1));
        end.Should().Be(new DateOnly(2027, 11, 30));
    }

    /// <summary>
    /// An out-of-range month degrades to calendar rather than throwing: this runs inside background sweeps over
    /// every tenant, and one corrupt row must not halt the job for everyone. (The org-profile validator bounds
    /// it to 1–12 at the write boundary.)
    /// </summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void OutOfRangeStartMonth_DegradesToCalendar_NeverThrows(int month)
    {
        var (start, end) = LeaveYear.BoundsFor(2026, month);

        start.Should().Be(new DateOnly(2026, 1, 1));
        end.Should().Be(new DateOnly(2026, 12, 31));
    }

    // ══ LabelFor — which leave year a date belongs to ══

    /// <summary>
    /// CONTROL: for a January tenant the label IS the calendar year — so `DateTime.UtcNow.Year`, which the
    /// accrual job used, stays correct for them.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData("2026-01-01", 2026)]
    [InlineData("2026-07-15", 2026)]
    [InlineData("2026-12-31", 2026)]
    public void Calendar_LabelIsTheCalendarYear(string date, int expected)
        => LeaveYear.LabelFor(DateOnly.Parse(date), fiscalYearStartMonth: 1).Should().Be(expected);

    /// <summary>
    /// TC-LV-264 (ISSUE-305): for an April tenant a leave year is labelled by the calendar year it STARTS in.
    /// So 2027-02-10 belongs to leave year **2026** (2026-04-01..2027-03-31), NOT 2027 — the exact case
    /// `DateTime.UtcNow.Year` gets wrong, and the reason the accrual job accrued into the wrong year for an
    /// Apr–Mar tenant every Jan–Mar.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData("2026-04-01", 2026)]   // first day of FY2026
    [InlineData("2026-12-31", 2026)]   // still FY2026
    [InlineData("2027-01-01", 2026)]   // ⚠ calendar 2027, leave year 2026
    [InlineData("2027-03-31", 2026)]   // last day of FY2026
    [InlineData("2027-04-01", 2027)]   // first day of FY2027
    [InlineData("2026-03-31", 2025)]   // the day before FY2026 began
    public void AprilTenant_LabelIsTheYearTheLeaveYearStartsIn(string date, int expected)
        => LeaveYear.LabelFor(DateOnly.Parse(date), fiscalYearStartMonth: 4).Should().Be(expected);

    /// <summary>Every date falls inside the bounds of the year LabelFor assigns it — the two must agree.</summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(12)]
    public void LabelFor_AndBoundsFor_Agree(int month)
    {
        for (var d = new DateOnly(2026, 1, 1); d <= new DateOnly(2027, 12, 31); d = d.AddDays(17))
        {
            var label = LeaveYear.LabelFor(d, month);
            var (start, end) = LeaveYear.BoundsFor(label, month);

            d.Should().BeOnOrAfter(start).And.BeOnOrBefore(
                end, $"{d} must fall inside the bounds of the leave year LabelFor gave it ({label})");
        }
    }

    /// <summary>
    /// StartOf is the carry-forward expiry anchor. Asserts the LITERAL, not <c>BoundsFor(...).Start</c> —
    /// StartOf is DEFINED as that expression, so comparing the two was a tautology that passed even when
    /// BoundsFor's start was mutated (caught by the test-authenticator audit).
    /// </summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData(1, 2026, 1, 1)]
    [InlineData(4, 2026, 4, 1)]
    [InlineData(12, 2026, 12, 1)]
    public void StartOf_IsTheFirstOfTheTenantsStartMonth(int month, int y, int m, int d)
        => LeaveYear.StartOf(2026, month).Should().Be(new DateOnly(y, m, d));

    // ══ Pro-rata over the tenant's own leave year (US-LV-002 AC-4) ══

    /// <summary>
    /// TC-LV-264 (ISSUE-305): a 1-Oct joiner is MID-year for an Apr-Mar tenant (6 of 12 months remain) but
    /// nearly-done for a January tenant (3 of 12). Pro-rata must therefore differ, and it is a MONEY number —
    /// the accrual job credits the ledger with it.
    ///
    /// <para>This is the arm that was missing: reverting CalculateProRata to a hardcoded
    /// <c>new DateTime(leaveYear,1,1)..(,12,31)</c> left the whole suite green, because no test anywhere
    /// passed a non-1 month.</para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public void ProRata_ForAnOctoberJoiner_DiffersBetweenAFiscalAndACalendarTenant()
    {
        var dateOfJoining = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        var calendar = LeaveEntitlementEngine.CalculateProRata(
            12m, dateOfJoining, leaveYear: 2026, fte: 1.0m, fiscalYearStartMonth: 1);
        var fiscal = LeaveEntitlementEngine.CalculateProRata(
            12m, dateOfJoining, leaveYear: 2026, fte: 1.0m, fiscalYearStartMonth: 4);

        fiscal.Should().BeGreaterThan(calendar,
            "an Apr-Mar leave year 2026 runs to 2027-03-31, so a 1-Oct joiner has ~6 months left; the same "
            + "joiner has only ~3 months of a Jan-Dec 2026 year");
        calendar.Should().BeLessThan(12m);
    }

    /// <summary>
    /// CONTROL: month 1 is byte-identical to the pre-ISSUE-305 hardcoded calendar window. A joiner ON the
    /// first day of the leave year gets the FULL entitlement under both the old literals and the new bounds.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData(1, "2026-01-01")]   // calendar tenant, joins on their day 1
    [InlineData(4, "2026-04-01")]   // fiscal tenant, joins on THEIR day 1
    public void ProRata_AJoinerOnDayOneOfTheirLeaveYear_GetsTheFullEntitlement(int month, string doj)
    {
        var prorated = LeaveEntitlementEngine.CalculateProRata(
            12m, DateTime.Parse(doj), leaveYear: 2026, fte: 1.0m, fiscalYearStartMonth: month);

        prorated.Should().Be(12m, "joining on the first day of the leave year is not a partial year");
    }

    /// <summary>
    /// An employee who joined BEFORE the leave year opened is a full-year employee, whichever basis applies.
    /// Pins that the fiscal window does not accidentally pro-rate existing staff.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData(1)]
    [InlineData(4)]
    public void ProRata_APreExistingEmployee_IsNeverProRated(int month)
        => LeaveEntitlementEngine.CalculateProRata(
            12m, new DateTime(2020, 1, 1), leaveYear: 2026, fte: 1.0m, fiscalYearStartMonth: month)
            .Should().Be(12m);
}
