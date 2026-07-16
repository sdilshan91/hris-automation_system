namespace HRM.Domain.Leave;

/// <summary>
/// ISSUE-305 (US-LV-002/006/008): the ONE definition of a tenant's leave-year boundaries.
///
/// <para>The leave subsystem universally hardcoded the leave year to calendar 1 Jan – 31 Dec — in
/// <c>LeaveAccrualJob</c>, <c>ProcessLeaveYearEndJob</c>, <c>LeaveCarryForwardCalculator.ComputeExpiryDate</c>
/// and <c>LeaveEntitlementEngine.CalculateProRata</c> — even though <c>Tenant.FiscalYearStartMonth</c> has
/// existed (default 1) and US-LV-002/006/008 specify the leave year as calendar-<b>or</b>-fiscal per tenant.
/// The setting was settable in the org-profile UI and read by NOTHING: a tenant could choose April and see no
/// effect whatsoever.</para>
///
/// <para><b>The year LABEL is unchanged.</b> A leave year is still identified by an <c>int</c> (the value on
/// <c>LeaveLedger.LeaveYear</c>); only its BOUNDS move. Leave year 2026 for a January tenant is
/// 2026-01-01..2026-12-31; for an April tenant it is 2026-04-01..2027-03-31 — i.e. the year is labelled by the
/// calendar year in which it STARTS. Keeping the label an int is what makes this change additive: no ledger
/// row is renumbered.</para>
///
/// <para><b>A January tenant (the default, and every tenant that never configured this) is byte-identical to
/// the pre-ISSUE-305 behaviour</b> — <see cref="BoundsFor"/> with month 1 returns exactly
/// <c>new DateOnly(year,1,1)..new DateOnly(year,12,31)</c>. That is the no-regression contract.</para>
///
/// <para>Pure and framework-free so the boundary arithmetic — the part that is easy to get wrong at the
/// December/January and February/leap-year edges — is unit-testable in isolation, mirroring
/// <c>LeaveEntitlementEngine</c> and <c>FiscalYearResolver</c>.</para>
/// </summary>
public static class LeaveYear
{
    /// <summary>The calendar leave year — the behaviour every site hardcoded before ISSUE-305.</summary>
    public const int CalendarStartMonth = 1;

    /// <summary>
    /// The inclusive bounds of leave year <paramref name="leaveYear"/> for a tenant whose fiscal year starts
    /// in <paramref name="fiscalYearStartMonth"/> (1–12; 1 = calendar).
    ///
    /// <para>The year runs from the 1st of that month in <paramref name="leaveYear"/> to the day before the
    /// same month's 1st in the following calendar year. April → 2026-04-01..2027-03-31. January → the plain
    /// calendar year (the end lands on 31 Dec of the SAME year, not the next).</para>
    ///
    /// <para>An out-of-range month is treated as calendar rather than throwing: this runs inside background
    /// sweeps over every tenant, and one corrupt row must not halt the job for everyone. The org-profile
    /// validator bounds the value to 1–12 at the write boundary.</para>
    /// </summary>
    public static (DateOnly Start, DateOnly End) BoundsFor(int leaveYear, int fiscalYearStartMonth)
    {
        var month = fiscalYearStartMonth is >= 1 and <= 12 ? fiscalYearStartMonth : CalendarStartMonth;

        var start = new DateOnly(leaveYear, month, 1);
        // The day before the next year's start. AddYears(1).AddDays(-1) handles the January case (→ 31 Dec of
        // the same year) and every leap-year edge without a special case.
        var end = start.AddYears(1).AddDays(-1);
        return (start, end);
    }

    /// <summary>
    /// Which leave year <paramref name="date"/> falls in, for a tenant starting in
    /// <paramref name="fiscalYearStartMonth"/>. A leave year is labelled by the calendar year it STARTS in, so
    /// for an April tenant 2027-02-10 belongs to leave year <b>2026</b> (2026-04-01..2027-03-31), not 2027.
    ///
    /// <para>This is the piece the jobs need: <c>DateTime.UtcNow.Year</c> is only the right leave year for a
    /// January tenant.</para>
    /// </summary>
    public static int LabelFor(DateOnly date, int fiscalYearStartMonth)
    {
        var month = fiscalYearStartMonth is >= 1 and <= 12 ? fiscalYearStartMonth : CalendarStartMonth;

        // Before the start month, the date still belongs to the year that began LAST calendar year.
        return date.Month >= month ? date.Year : date.Year - 1;
    }

    /// <summary>
    /// The first day of leave year <paramref name="leaveYear"/> — the anchor carry-forward expiry counts from
    /// (<c>LeaveCarryForwardCalculator.ComputeExpiryDate</c>).
    /// </summary>
    public static DateOnly StartOf(int leaveYear, int fiscalYearStartMonth)
        => BoundsFor(leaveYear, fiscalYearStartMonth).Start;
}
