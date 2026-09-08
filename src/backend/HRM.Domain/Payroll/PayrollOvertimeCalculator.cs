using System.Globalization;

namespace HRM.Domain.Payroll;

/// <summary>
/// Pure, deterministic overtime-earnings calculator (US-PAY-010 AC-2/FR-4). No DB / tenant / clock access, so
/// it is fully unit-testable (NFR-3 &gt;= 85%). Given an employee's monthly BASIC, the period working-days, and
/// the attendance module's approved-overtime minutes broken down per multiplier bucket (US-ATT-009
/// <c>OvertimeMultiplierDetails</c>), it derives the base hourly rate and produces the overtime earning.
///
/// <para>Computation model (FR-4 / BR-5):</para>
/// <list type="number">
///   <item><c>hourly_rate = monthly_basic / (working_days * standard_hours_per_day)</c>, with a documented
///         <see cref="StandardHoursPerDay"/> of 8. This is the base rate the tenant OT multipliers scale.</item>
///   <item>For each multiplier bucket, <c>amount = (minutes / 60) * hourly_rate * multiplier</c>. Holiday OT
///         is simply a bucket carrying the holiday multiplier (e.g. "2" for 2x, BR-5) — no special-casing.</item>
///   <item><c>overtime_amount</c> = sum over buckets (rounded half-up to 2 dp); <c>overtime_hours</c> = total
///         approved minutes / 60 (rounded to 2 dp).</item>
/// </list>
///
/// <para>BR-4 (pre-approval) is enforced UPSTREAM: the attendance pull surfaces APPROVED minutes only, so this
/// calculator never sees pending/rejected overtime. A zero/empty bucket set yields a zero result and no line.</para>
/// </summary>
public static class PayrollOvertimeCalculator
{
    /// <summary>The documented standard working-hours per day used to derive the base hourly rate (FR-4).</summary>
    public const decimal StandardHoursPerDay = 8m;

    /// <summary>The label used for the overtime earning line on the slip.</summary>
    public const string OvertimeLineName = "Overtime";

    /// <summary>
    /// The documented overtime multiplier applied when neither the attendance record nor the tenant supplies a
    /// usable one — i.e. the value of <c>defaultMultiplier</c> when the caller omits it. Deliberately equal to
    /// <c>AttendanceSettings.WeekdayOvertimeMultiplier</c>'s own default (1.50), so a tenant with no attendance
    /// policy row is priced exactly as a tenant that saved the defaults.
    ///
    /// <para>BUG-456: exists so the payroll caller can express "no policy row ⇒ the code default" without
    /// re-declaring the literal 1.5 next to a call that already has a 1.5 default. Two copies of a money
    /// constant drift; one does not.</para>
    /// </summary>
    public const decimal DefaultOvertimeMultiplier = 1.5m;

    /// <summary>The result of an overtime computation for one employee in one period (US-PAY-010 AC-2).</summary>
    /// <param name="OvertimeHours">Total approved overtime hours (minutes / 60), 2 dp.</param>
    /// <param name="OvertimeAmount">Total overtime earning across all multiplier buckets, 2 dp.</param>
    /// <param name="HourlyRate">The derived base hourly rate (for the calculation-basis string), 2 dp.</param>
    public readonly record struct OvertimeResult(decimal OvertimeHours, decimal OvertimeAmount, decimal HourlyRate)
    {
        public static readonly OvertimeResult Zero = new(0m, 0m, 0m);
        public bool IsZero => OvertimeAmount == 0m && OvertimeHours == 0m;
    }

    /// <summary>
    /// Computes the overtime earning for one employee. <paramref name="multiplierBuckets"/> maps the multiplier
    /// (e.g. "1.5", "2") to APPROVED minutes at that rate (US-ATT-009 <c>OvertimeMultiplierDetails</c>). When the
    /// per-multiplier breakdown is empty but <paramref name="totalApprovedMinutes"/> is positive (older
    /// attendance data without a breakdown), the whole block is treated at the
    /// <paramref name="defaultMultiplier"/>. Returns <see cref="OvertimeResult.Zero"/> for no OT.
    ///
    /// <para><b>US-ATT-011 AC-5 / US-CHR-013 — FTE-scaled base.</b> When <paramref name="fteScaledBase"/> is
    /// true the hourly base divides by <c>workingDays * standard_hours * fte</c>, so a 0.5-FTE employee on the
    /// same monthly basic gets a 2x hourly rate (that basic buys half the hours). When it is false — <b>the
    /// default</b> — <paramref name="fte"/> is ignored entirely and the rate is byte-identical to its
    /// pre-US-CHR-013 value. The caller resolves the flag + the employee's FTE and passes them in; this stays
    /// pure, exactly as the work-week and holiday multipliers are already handled.</para>
    /// </summary>
    /// <param name="defaultMultiplier">
    /// Tenant policy <c>AttendanceSettings.WeekdayOvertimeMultiplier</c>, resolved by the caller off the
    /// employee's EFFECTIVE policy row (BUG-456). <b>Strictly subordinate to the per-record rate:</b> a bucket
    /// whose key parses to a positive decimal is paid at THAT rate and never at this one. This applies only
    /// where no usable per-record multiplier exists — an empty breakdown with positive approved minutes, or a
    /// bucket key that is not a positive decimal. Getting that precedence backwards would reprice every record
    /// that already carries an explicit rate. Defaults to <see cref="DefaultOvertimeMultiplier"/>.
    /// </param>
    /// <param name="fte">The employee's full-time equivalent. Ignored unless <paramref name="fteScaledBase"/>.</param>
    /// <param name="fteScaledBase">Tenant policy <c>AttendanceSettings.FteScaledOvertimeBase</c>. Default false.</param>
    public static OvertimeResult Compute(
        decimal monthlyBasic,
        decimal workingDays,
        IReadOnlyDictionary<string, int> multiplierBuckets,
        int totalApprovedMinutes,
        decimal defaultMultiplier = DefaultOvertimeMultiplier,
        decimal fte = 1.0m,
        bool fteScaledBase = false)
    {
        if (monthlyBasic <= 0m || workingDays <= 0m)
            return OvertimeResult.Zero;

        // Off (default) → the divisor is untouched. On → scale by FTE. A non-positive FTE is not scaled: it
        // would divide by zero / invert the rate, and 0 is not a valid FTE (the create/update validators
        // reject it), so an unscaled rate is the safe reading of a corrupt row.
        var effectiveHoursPerDay = fteScaledBase && fte > 0m
            ? StandardHoursPerDay * fte
            : StandardHoursPerDay;

        var hourlyRate = monthlyBasic / (workingDays * effectiveHoursPerDay);

        decimal totalAmount = 0m;
        int totalMinutes = 0;

        if (multiplierBuckets is { Count: > 0 })
        {
            foreach (var (key, minutes) in multiplierBuckets)
            {
                if (minutes <= 0) continue;
                var multiplier = ParseMultiplier(key, defaultMultiplier);
                totalAmount += (minutes / 60m) * hourlyRate * multiplier;
                totalMinutes += minutes;
            }
        }
        else if (totalApprovedMinutes > 0)
        {
            // No per-multiplier breakdown — treat the whole approved block at the default multiplier.
            totalAmount = (totalApprovedMinutes / 60m) * hourlyRate * defaultMultiplier;
            totalMinutes = totalApprovedMinutes;
        }

        if (totalMinutes <= 0)
            return OvertimeResult.Zero;

        return new OvertimeResult(
            Round(totalMinutes / 60m),
            Round(totalAmount),
            Round(hourlyRate));
    }

    private static decimal ParseMultiplier(string key, decimal fallback)
        => decimal.TryParse(key, NumberStyles.Number, CultureInfo.InvariantCulture, out var m) && m > 0m
            ? m
            : fallback;

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
