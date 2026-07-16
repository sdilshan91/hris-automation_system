// ============================================================================
// CAL-8 / ISSUE-305 — LeaveAccrualJob's per-tenant leave-year label.
//
// This site had ZERO tests until now (found by the @test-authenticator: mutant M6 — reverting the label to
// `DateTime.UtcNow.Year` — survived a green suite). It is the ledger's CREDIT side: if it regresses, an
// Apr–Mar tenant's accrual lands in leave year 2027 while every debit reads 2026, which recreates the exact
// credit/debit split this epic exists to fix, from the other end. Silent, and only for three months a year.
//
// The job body sweeps every tenant and cannot be driven against shared data, so the per-tenant DECISION is
// tested through the extracted seam — same approach as ProcessLeaveYearEndJob.ClosingLeaveYearIfDue.
// ============================================================================

using FluentAssertions;
using HRM.Api.Jobs;

namespace HRM.Tests.Unit;

public sealed class LeaveAccrualJobLeaveYearTests
{
    // ══ Calendar tenant — the no-regression contract ══

    /// <summary>
    /// CONTROL: for a January tenant the accrual label IS the calendar year — byte-identical to the
    /// `DateTime.UtcNow.Year` this replaced. Every tenant that never configured a fiscal year lands here, so
    /// this arm is what proves the change is additive for them.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData("2026-01-01", 2026)]
    [InlineData("2026-07-15", 2026)]
    [InlineData("2026-12-31", 2026)]
    [InlineData("2027-02-10", 2027)]
    public void CalendarTenant_AccruesIntoTheCalendarYear(string today, int expected)
        => LeaveAccrualJob.AccrualLeaveYearFor(DateOnly.Parse(today), fiscalYearStartMonth: 1)
            .Should().Be(expected);

    // ══ Fiscal tenant — the ISSUE-305 defect ══

    /// <summary>
    /// THE regression arm. On 2027-02-10 an Apr–Mar tenant is inside leave year **2026** (2026-04-01 ..
    /// 2027-03-31), so that is where the accrual must be credited. `UtcNow.Year` would say 2027 — a year that
    /// has not started — and the credit would land in a bucket no leave request reads.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public void FiscalTenant_InTheJanToMarWindow_AccruesIntoTheYearThatIsStillOpen()
    {
        var label = LeaveAccrualJob.AccrualLeaveYearFor(new DateOnly(2027, 2, 10), fiscalYearStartMonth: 4);

        label.Should().Be(2026, "2027-02-10 is inside the Apr–Mar tenant's leave year 2026");
        label.Should().NotBe(2027, "2027 is exactly what the pre-ISSUE-305 UtcNow.Year produced, and it is wrong");
    }

    /// <summary>
    /// The two tenants differ ONLY by the column, so on the same day they must accrue into DIFFERENT years.
    /// This fails if the label stops reading FiscalYearStartMonth — no amount of correct arithmetic elsewhere
    /// can make it pass.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public void OnTheSameDay_AFiscalAndACalendarTenantAccrueIntoDifferentYears()
    {
        var today = new DateOnly(2027, 2, 10);

        var calendar = LeaveAccrualJob.AccrualLeaveYearFor(today, fiscalYearStartMonth: 1);
        var fiscal = LeaveAccrualJob.AccrualLeaveYearFor(today, fiscalYearStartMonth: 4);

        fiscal.Should().NotBe(calendar, "if these match, the tenant's column is being ignored again (ISSUE-305)");
        (calendar, fiscal).Should().Be((2027, 2026));
    }

    /// <summary>
    /// Across the fiscal boundary the label advances exactly once, on 1 April — not on 1 January. The 31-Mar/
    /// 1-Apr pair is the edge the whole epic turns on.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData("2026-03-31", 2025)]   // last day of FY2025
    [InlineData("2026-04-01", 2026)]   // FY2026 opens
    [InlineData("2026-12-31", 2026)]   // ⚠ still FY2026 — a calendar rollover must NOT advance it
    [InlineData("2027-01-01", 2026)]   // ⚠ still FY2026
    [InlineData("2027-03-31", 2026)]   // last day of FY2026
    [InlineData("2027-04-01", 2027)]   // FY2027 opens
    public void FiscalTenant_LabelAdvancesOn1April_NotOn1January(string today, int expected)
        => LeaveAccrualJob.AccrualLeaveYearFor(DateOnly.Parse(today), fiscalYearStartMonth: 4)
            .Should().Be(expected);

    /// <summary>
    /// The accrual label and the year-end job's closing year must agree on the same clock: on a tenant's
    /// opening day, the year the accrual credits is exactly the year the year-end job has just closed, plus
    /// one. If these two jobs ever drift apart, a whole leave year is either accrued twice or never.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData(1, "2027-01-01")]
    [InlineData(4, "2027-04-01")]
    public void TheAccrualLabelAndTheYearEndClosingYear_Agree(int month, string openingDay)
    {
        var today = DateOnly.Parse(openingDay);

        var accrualYear = LeaveAccrualJob.AccrualLeaveYearFor(today, month);
        var closingYear = ProcessLeaveYearEndJob.ClosingLeaveYearIfDue(today, month)!.Value;

        accrualYear.Should().Be(closingYear + 1,
            "on the opening day the year-end job closes year N and the accrual job must credit year N+1");
    }

    /// <summary>
    /// A corrupt out-of-range month degrades to calendar rather than throwing — this runs inside a sweep over
    /// every tenant and one bad row must not abort accrual for everyone else.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData(0)]
    [InlineData(13)]
    public void OutOfRangeStartMonth_DegradesToCalendar_NeverThrows(int month)
        => LeaveAccrualJob.AccrualLeaveYearFor(new DateOnly(2027, 2, 10), month).Should().Be(2027);
}
