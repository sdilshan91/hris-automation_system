// ============================================================================
// CAL-8 / ISSUE-305: ProcessLeaveYearEndJob.ClosingLeaveYearIfDue — the per-tenant year-end boundary.
//
// The job used to fire on a "0 2 1 1 *" cron and assume `UtcNow.Year - 1` was the closing year. For an
// Apr-Mar tenant BOTH were wrong, and the schedule was the un-fixable half: no matter what the job computed
// internally, a 1-January cron can never run on 1 April. The job now runs daily and this method decides who
// is due.
//
// Two directions have to hold, and each is a real failure mode:
//   * due when it should be    -> miss it and the tenant's carry-forward silently does not happen for a YEAR
//                                 (the next run is 12 months later), quietly forfeiting employees' balances.
//   * NOT due when it isn't    -> fire early/late and it closes the WRONG year, or re-closes a year using
//                                 balances that have since moved.
// ============================================================================

using FluentAssertions;
using HRM.Api.Jobs;

namespace HRM.Tests.Unit;

public sealed class ProcessLeaveYearEndJobWindowTests
{
    // ══ Calendar tenant — the no-regression contract ══

    /// <summary>
    /// CONTROL: a January tenant is still due on 1 January, closing the previous calendar year — byte-identical
    /// to what the old "0 2 1 1 *" cron + `UtcNow.Year - 1` did. Every tenant that never configured a fiscal
    /// year must land here.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public void CalendarTenant_IsDueOn1January_ClosingThePreviousYear()
        => ProcessLeaveYearEndJob.ClosingLeaveYearIfDue(new DateOnly(2027, 1, 1), fiscalYearStartMonth: 1)
            .Should().Be(2026, "the year that just closed on 31 Dec 2026");

    /// <summary>A January tenant is not due in the middle of their year — the sweep must be a no-op that day.</summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData("2026-04-01")]   // ⚠ the fiscal tenant's boundary — must NOT trigger a calendar tenant
    [InlineData("2026-07-15")]
    [InlineData("2026-12-31")]   // the last day of the closing year: the year has not ended YET
    public void CalendarTenant_IsNotDueMidYear(string date)
        => ProcessLeaveYearEndJob.ClosingLeaveYearIfDue(DateOnly.Parse(date), fiscalYearStartMonth: 1)
            .Should().BeNull();

    // ══ Fiscal tenant — the ISSUE-305 defect ══

    /// <summary>
    /// TC-LV-264 (ISSUE-305), THE regression arm: an Apr-Mar tenant is due on 1 April, closing leave year 2025
    /// (2025-04-01..2026-03-31). Under the old 1-January cron this day never triggered the job at all.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public void AprilTenant_IsDueOn1April_ClosingThePreviousFiscalYear()
        => ProcessLeaveYearEndJob.ClosingLeaveYearIfDue(new DateOnly(2026, 4, 1), fiscalYearStartMonth: 4)
            .Should().Be(2025, "leave year 2025 ran 2025-04-01..2026-03-31 and closed yesterday");

    /// <summary>
    /// TC-LV-264 (ISSUE-305), the mirror arm: an Apr-Mar tenant is NOT due on 1 January — the one day the old
    /// cron DID fire. Back then it ran mid-year and closed the wrong year (`2026-1 = 2025`, whose 2025-04-01
    /// leave year was still three months from ending).
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public void AprilTenant_IsNotDueOn1January_TheDayTheOldCronFired()
        => ProcessLeaveYearEndJob.ClosingLeaveYearIfDue(new DateOnly(2026, 1, 1), fiscalYearStartMonth: 4)
            .Should().BeNull("1 Jan is the MIDDLE of an Apr-Mar tenant's leave year");

    /// <summary>
    /// The day before the boundary — 31 March — must not be due: the leave year has not finished yet, and a
    /// balance can still change on its final day.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public void AprilTenant_IsNotDueOnTheLastDayOfTheLeaveYear()
        => ProcessLeaveYearEndJob.ClosingLeaveYearIfDue(new DateOnly(2026, 3, 31), fiscalYearStartMonth: 4)
            .Should().BeNull("31 March is still IN leave year 2025 — it closes at the END of that day");

    /// <summary>Every start month is due on its own 1st, and closes the year before.</summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData(1, "2026-01-01", 2025)]
    [InlineData(4, "2026-04-01", 2025)]
    [InlineData(7, "2026-07-01", 2025)]
    [InlineData(12, "2026-12-01", 2025)]
    public void AnyTenant_IsDueOnTheFirstOfTheirOwnStartMonth(int month, string date, int expectedClosing)
        => ProcessLeaveYearEndJob.ClosingLeaveYearIfDue(DateOnly.Parse(date), month)
            .Should().Be(expectedClosing);

    // ══ The grace window ══

    /// <summary>
    /// A missed or failed run on the opening day must still catch up: days 2..7 of the new leave year are due,
    /// for the SAME closing year. Without the window, one failed daily run would defer a tenant's entire
    /// year-end by twelve months.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData("2026-04-02")]
    [InlineData("2026-04-05")]
    [InlineData("2026-04-07")]   // last day inside the 7-day window
    public void AprilTenant_StillCatchesUpInsideTheGraceWindow(string date)
        => ProcessLeaveYearEndJob.ClosingLeaveYearIfDue(DateOnly.Parse(date), fiscalYearStartMonth: 4)
            .Should().Be(2025, "a missed opening-day run must still close the same year");

    /// <summary>
    /// The window CLOSES. Past it the job stops re-selecting the tenant every day for the rest of the year —
    /// the idempotent per-pair skip would make that harmless but it would scan every employee daily forever.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData("2026-04-08")]   // first day past the window
    [InlineData("2026-06-01")]
    public void AprilTenant_IsNotDueOnceTheWindowHasPassed(string date)
        => ProcessLeaveYearEndJob.ClosingLeaveYearIfDue(DateOnly.Parse(date), fiscalYearStartMonth: 4)
            .Should().BeNull();

    /// <summary>
    /// Across two full calendar years, EVERY tenant is due on exactly the number of days in the grace window —
    /// never zero (a skipped year-end) and never "most days" (a window that swallowed the year). This is the
    /// property arm: it fails both if the window never opens and if it never closes.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(12)]
    public void EveryTenant_IsDueOnExactlyTheWindowLength_PerYear(int month)
    {
        var dueDays = new List<DateOnly>();
        for (var d = new DateOnly(2026, 1, 1); d <= new DateOnly(2027, 12, 31); d = d.AddDays(1))
        {
            if (ProcessLeaveYearEndJob.ClosingLeaveYearIfDue(d, month) is not null)
                dueDays.Add(d);
        }

        dueDays.Should().HaveCount(14, "7 due days in each of the 2 years covered");
        dueDays.Select(d => d.Month).Distinct().Should().Equal(month);
        dueDays.Select(d => ProcessLeaveYearEndJob.ClosingLeaveYearIfDue(d, month)!.Value)
            .Distinct().Should().HaveCount(2, "each of the 2 windows closes exactly one distinct year");
    }

    /// <summary>
    /// The closing year the job hands to ProcessYearEndAsync must be the year whose bounds ENDED yesterday —
    /// i.e. the window and LeaveYear agree. A drift of one here silently carries forward the wrong ledger.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData(1, "2026-01-01")]
    [InlineData(4, "2026-04-01")]
    public void TheClosingYearIsTheOneThatEndedYesterday(int month, string openingDay)
    {
        var today = DateOnly.Parse(openingDay);
        var closing = ProcessLeaveYearEndJob.ClosingLeaveYearIfDue(today, month)!.Value;

        HRM.Domain.Leave.LeaveYear.BoundsFor(closing, month).End
            .Should().Be(today.AddDays(-1), "the closing year must end the day before its successor opens");
    }

    /// <summary>
    /// A corrupt out-of-range month degrades to calendar rather than throwing — this runs inside a sweep over
    /// every tenant, and one bad row must not abort the year-end for everyone else.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData(0)]
    [InlineData(13)]
    public void OutOfRangeStartMonth_DegradesToCalendar_NeverThrows(int month)
    {
        ProcessLeaveYearEndJob.ClosingLeaveYearIfDue(new DateOnly(2026, 1, 1), month).Should().Be(2025);
        ProcessLeaveYearEndJob.ClosingLeaveYearIfDue(new DateOnly(2026, 4, 1), month).Should().BeNull();
    }
}
