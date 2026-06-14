namespace HRM.Domain.Entities;

/// <summary>
/// Outcome of closing an attendance session (US-ATT-002 FR-2..FR-4/FR-7, BR-2..BR-4/BR-6).
/// </summary>
public readonly record struct AttendanceCalculation(
    int TotalWorkMinutes,
    int OvertimeMinutes,
    string Status);

/// <summary>
/// Pure work-hours calculation for a closed attendance session (US-ATT-002). Lives in the Domain
/// layer (no framework deps) so both the clock-out service path and the auto-clock-out safety-net
/// job (BR-5) apply identical, minute-accurate (NFR-2) rules against the tenant policy
/// (<see cref="AttendanceSettings"/>).
///
/// Policy thresholds come from <see cref="AttendanceSettings"/> as a tenant-level fallback.
/// TODO(US-ATT-005 follow-up): the <see cref="Shift"/> entity now exists (US-ATT-005). Wiring the
/// resolved shift's break/grace/minimum-hours into this calculation is deferred — the shift model
/// carries BreakDurationMinutes / MinimumHours / GracePeriodMinutes but NOT the calculator's
/// StandardWorkMinutes / AutoBreakThresholdMinutes / OvertimeThresholdMinutes, so a clean mapping
/// needs those fields added to Shift (or a derived policy). Until then the tenant-level
/// AttendanceSettings remains the source for clock-out work-hours math (see vault note).
/// </summary>
public static class AttendanceCalculator
{
    /// <summary>
    /// A session whose gross span exceeds this is flagged ANOMALY (US-ATT-002 BR-6, FR-7).
    /// </summary>
    public const int AnomalyThresholdMinutes = 16 * 60;

    /// <summary>
    /// Computes net worked minutes (after auto-break deduction), overtime minutes, and the outcome
    /// status for a session running from <paramref name="clockIn"/> to <paramref name="clockOut"/>.
    /// </summary>
    /// <param name="isSystemClosed">
    /// True when closed by the auto-clock-out job (BR-5) — always classified ANOMALY for regularization.
    /// </param>
    public static AttendanceCalculation Calculate(
        DateTime clockIn,
        DateTime clockOut,
        AttendanceSettings settings,
        bool isSystemClosed = false)
    {
        // NFR-2: minute-accurate. Truncate to whole minutes (round toward zero); never negative.
        var grossMinutes = (int)Math.Max(0, Math.Floor((clockOut - clockIn).TotalMinutes));

        // FR-3 / BR-2: auto-deduct the configured break once the gross session exceeds the threshold.
        var breakMinutes = grossMinutes > settings.AutoBreakThresholdMinutes
            ? settings.AutoBreakMinutes
            : 0;

        var totalWorkMinutes = Math.Max(0, grossMinutes - breakMinutes);

        // FR-4 / BR-3: overtime is net work beyond standard + the tolerance threshold.
        var overtimeMinutes = Math.Max(
            0, totalWorkMinutes - (settings.StandardWorkMinutes + settings.OvertimeThresholdMinutes));

        // Status precedence: ANOMALY (system close or >16h span) > SHORT_DAY > OVERTIME > COMPLETE.
        string status;
        if (isSystemClosed || grossMinutes > AnomalyThresholdMinutes)
            status = "ANOMALY";                                   // FR-7 / BR-6 / BR-5
        else if (totalWorkMinutes < settings.MinimumWorkMinutes)
            status = "SHORT_DAY";                                 // BR-4
        else if (overtimeMinutes > 0)
            status = "OVERTIME";                                  // BR-3
        else
            status = "COMPLETE";

        return new AttendanceCalculation(totalWorkMinutes, overtimeMinutes, status);
    }
}
