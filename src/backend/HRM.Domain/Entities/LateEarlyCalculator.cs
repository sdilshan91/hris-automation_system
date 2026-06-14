namespace HRM.Domain.Entities;

/// <summary>
/// Outcome of evaluating a single attendance punch for lateness / early departure (US-ATT-008
/// FR-1/FR-2/FR-3, BR-1/BR-2/BR-3/BR-6).
/// </summary>
/// <param name="IsLate">True when the clock-in is past shift start + grace (BR-1).</param>
/// <param name="LateMinutes">Whole minutes past the shift START (AC-1: "late_minutes = 20"); 0 when on time or no shift start.</param>
/// <param name="LateByMinutes">Whole minutes past the grace threshold (AC-1: "late_by = 5"); 0 when not late.</param>
/// <param name="IsEarlyDeparture">True when the clock-out is before shift end and minimum hours are unmet (BR-2).</param>
/// <param name="EarlyDepartureMinutes">Whole minutes before shift END (AC-3: "early_departure_minutes = 30"); 0 when none.</param>
public readonly record struct LateEarlyResult(
    bool IsLate,
    int LateMinutes,
    int LateByMinutes,
    bool IsEarlyDeparture,
    int EarlyDepartureMinutes);

/// <summary>
/// Pure late-arrival / early-departure detection (US-ATT-008). Lives in the Domain layer (no
/// framework deps) so the inline clock-in/clock-out path (NFR-1), the regularization-approval
/// recompute (BR-7), the monthly-summary aggregation (US-ATT-007 read-of-persisted), and the unit
/// tests all apply identical rules — a single source of truth.
///
/// Rules (US-ATT-008 §6/§10):
///   - BR-1: late when clock_in &gt; shift_start + grace. <c>late_minutes</c> is the minutes past the
///     shift START (NOT past grace); the grace only gates the <c>is_late</c> flag (re-read AC-1:
///     09:20 vs 09:00 grace 15 → is_late, late_minutes=20, late_by=5).
///   - BR-2: early departure when clock_out &lt; shift_end AND the worked minutes are below the shift
///     minimum required hours. Grace does NOT apply to early departure (§10).
///   - BR-3: grace resolves shift → tenant default → 0 (resolved by the caller; passed in here).
///   - BR-6 / §10: FLEXIBLE shifts (no fixed start/end) are EXEMPT — no late/early flags.
/// Times are wall-clock TimeOnly; the caller supplies the punch instant's time-of-day. Overnight
/// (night-shift) wrap is not specially handled (deferred — same as the rest of the module's
/// tenant-timezone deferral); same-day comparison only.
/// </summary>
public static class LateEarlyCalculator
{
    /// <summary>
    /// Evaluates a punch against the resolved shift window.
    /// </summary>
    /// <param name="clockInTime">The clock-in wall-clock time (required for late detection).</param>
    /// <param name="clockOutTime">The clock-out wall-clock time, or null when the punch is still open.</param>
    /// <param name="shiftStart">Resolved shift start; null for FLEXIBLE (→ no late detection).</param>
    /// <param name="shiftEnd">Resolved shift end; null for FLEXIBLE (→ no early detection).</param>
    /// <param name="gracePeriodMinutes">Resolved grace minutes (shift, else tenant default, else 0). Late only (§10).</param>
    /// <param name="workedMinutes">Net worked minutes for the session; used with <paramref name="minimumMinutes"/> for BR-2.</param>
    /// <param name="minimumMinutes">Shift minimum required minutes (BR-2). When &lt;= 0, the minimum-hours gate is skipped.</param>
    public static LateEarlyResult Evaluate(
        TimeOnly clockInTime,
        TimeOnly? clockOutTime,
        TimeOnly? shiftStart,
        TimeOnly? shiftEnd,
        int gracePeriodMinutes,
        int? workedMinutes,
        int minimumMinutes)
    {
        bool isLate = false;
        int lateMinutes = 0, lateBy = 0;

        // BR-1 / AC-1: minutes past the shift START (grace gates the flag, not the magnitude).
        if (shiftStart is { } start)
        {
            var pastStart = (int)Math.Floor((clockInTime.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes);
            if (pastStart > 0)
            {
                lateMinutes = pastStart;
                isLate = pastStart > Math.Max(0, gracePeriodMinutes);   // BR-1: past start + grace.
                if (isLate)
                    lateBy = pastStart - Math.Max(0, gracePeriodMinutes);
            }
        }

        bool isEarly = false;
        int earlyMinutes = 0;

        // BR-2: clock-out before shift end AND minimum hours not met. Grace does NOT apply (§10).
        if (shiftEnd is { } end && clockOutTime is { } co)
        {
            var beforeEnd = (int)Math.Floor((end.ToTimeSpan() - co.ToTimeSpan()).TotalMinutes);
            if (beforeEnd > 0)
            {
                bool minimumUnmet = minimumMinutes <= 0
                    || (workedMinutes ?? int.MaxValue) < minimumMinutes;
                if (minimumUnmet)
                {
                    isEarly = true;
                    earlyMinutes = beforeEnd;
                }
            }
        }

        return new LateEarlyResult(isLate, lateMinutes, lateBy, isEarly, earlyMinutes);
    }
}
