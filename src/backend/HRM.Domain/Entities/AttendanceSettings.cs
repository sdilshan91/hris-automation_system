namespace HRM.Domain.Entities;

/// <summary>
/// Per-tenant attendance policy that drives clock-in enforcement (US-ATT-001 BR-2/BR-3/BR-6).
/// Exactly one row per tenant; created lazily with safe defaults (all enforcement off) the first
/// time a tenant clocks in if no row exists yet. Tenant-scoped via <see cref="BaseEntity.TenantId"/>.
/// Maps to the "attendance_settings" table.
/// </summary>
public sealed class AttendanceSettings : BaseEntity
{
    /// <summary>
    /// BR-2 / AC-3: when true, latitude+longitude are mandatory on clock-in and a clock-in
    /// without coordinates is rejected. When false, geolocation is optional (AC-4).
    /// </summary>
    public bool RequireGeolocation { get; set; }

    /// <summary>
    /// FR-3: when true, supplied coordinates are validated against the configured allowed
    /// location within <see cref="GeoFenceRadiusMeters"/>.
    /// </summary>
    public bool GeoFenceEnabled { get; set; }

    /// <summary>
    /// Allowed-location latitude used for the geo-fence check (FR-3). Null when not configured.
    /// </summary>
    public decimal? GeoFenceLatitude { get; set; }

    /// <summary>
    /// Allowed-location longitude used for the geo-fence check (FR-3). Null when not configured.
    /// </summary>
    public decimal? GeoFenceLongitude { get; set; }

    /// <summary>
    /// Radius in metres around the allowed location within which clock-in is permitted (FR-3).
    /// </summary>
    public int GeoFenceRadiusMeters { get; set; } = 100;

    /// <summary>
    /// BR-3 / AC-5: when true, the request source IP must be in <see cref="IpAllowlist"/>.
    /// </summary>
    public bool IpAllowlistEnabled { get; set; }

    /// <summary>
    /// Allowed source IP addresses (exact match) when <see cref="IpAllowlistEnabled"/> is true (FR-4).
    /// Stored as a PostgreSQL text[] column.
    /// </summary>
    public List<string> IpAllowlist { get; set; } = new();

    /// <summary>
    /// BR-6: when true, a selfie photo must be supplied before the clock-in is accepted.
    /// </summary>
    public bool RequirePhoto { get; set; }

    /// <summary>
    /// BR-4: grace period (minutes) after shift start within which a clock-in is not marked late.
    /// Stored for use by later lateness stories; not enforced in US-ATT-001.
    /// </summary>
    public int GracePeriodMinutes { get; set; }

    // ── Work-hours calculation policy (US-ATT-002) ─────────────────────
    // TODO(US-ATT-005): these are tenant-level fallbacks. Once shifts exist, standard/minimum
    // hours, break rules and overtime thresholds should be sourced from the assigned shift and
    // these become defaults for employees without an assigned shift.

    /// <summary>
    /// Standard scheduled work minutes for a full day (US-ATT-002 FR-4, BR-3). Net worked
    /// minutes beyond this (plus <see cref="OvertimeThresholdMinutes"/>) count as overtime.
    /// Default 480 (8 h).
    /// </summary>
    public int StandardWorkMinutes { get; set; } = 480;

    /// <summary>
    /// Minimum net worked minutes for a full day (US-ATT-002 FR-4, BR-4). Below this the
    /// session is flagged "SHORT_DAY" for HR review. Default 240 (4 h).
    /// </summary>
    public int MinimumWorkMinutes { get; set; } = 240;

    /// <summary>
    /// Break minutes auto-deducted from gross worked time when the gross session length exceeds
    /// <see cref="AutoBreakThresholdMinutes"/> (US-ATT-002 FR-3, BR-2). Default 60.
    /// </summary>
    public int AutoBreakMinutes { get; set; } = 60;

    /// <summary>
    /// Gross session length (minutes) above which <see cref="AutoBreakMinutes"/> is deducted
    /// (US-ATT-002 FR-3). Default 360 (6 h).
    /// </summary>
    public int AutoBreakThresholdMinutes { get; set; } = 360;

    /// <summary>
    /// Extra minutes beyond <see cref="StandardWorkMinutes"/> that are tolerated before any
    /// excess is classified as overtime (US-ATT-002 BR-3). Default 0 (any excess is overtime).
    /// </summary>
    public int OvertimeThresholdMinutes { get; set; }

    // ── Regularization policy (US-ATT-003) ─────────────────────────────

    /// <summary>
    /// Tenant-configurable lookback window (in calendar days) within which an attendance
    /// regularization request may be submitted (US-ATT-003 FR-6/BR-2/AC-3). Default 7. A request for
    /// a date older than this is rejected.
    /// </summary>
    public int RegularizationLookbackDays { get; set; } = 7;

    // ── Overtime rules (US-ATT-006 FR-3) ───────────────────────────────
    // Tenant-configurable. Reuses the existing OvertimeThresholdMinutes above for BR-1/BR-2; the rest
    // are new. The story's default threshold is 30 minutes (BR-2) — the US-ATT-002 default of 0 (any
    // excess is overtime) is retained for backward compatibility, and overtime detection adds an
    // explicit minimum-threshold gate (see AttendanceCalculator.CalculateOvertime).

    /// <summary>
    /// BR-2 default overtime threshold (minutes) when no shift-specific value applies (US-ATT-006).
    /// Work exceeding standard hours by less than this is not counted as overtime. Default 30.
    /// Distinct from <see cref="OvertimeThresholdMinutes"/> (US-ATT-002 tolerance, default 0) so the
    /// existing clock-out status precedence is unchanged; overtime-record detection uses this value.
    /// </summary>
    public int OvertimeMinimumThresholdMinutes { get; set; } = 30;

    /// <summary>BR-3: weekday overtime pay multiplier. Default 1.50.</summary>
    public decimal WeekdayOvertimeMultiplier { get; set; } = 1.5m;

    /// <summary>BR-3/BR-7: weekend (rest-day) overtime pay multiplier. Default 2.00.</summary>
    public decimal WeekendOvertimeMultiplier { get; set; } = 2.0m;

    /// <summary>BR-3/BR-7: public-holiday overtime pay multiplier. Default 2.50.</summary>
    public decimal HolidayOvertimeMultiplier { get; set; } = 2.5m;

    /// <summary>BR-4: maximum daily overtime (minutes). Overtime beyond this is capped and flagged. Default 240 (4 h).</summary>
    public int MaxDailyOvertimeMinutes { get; set; } = 240;

    /// <summary>BR-5: maximum weekly overtime (minutes); an HR alert is raised on approach. Default 1200 (20 h).</summary>
    public int MaxWeeklyOvertimeMinutes { get; set; } = 1200;

    /// <summary>
    /// FR-4/AC-2/BR-6: when true, employees must submit an overtime pre-approval request before working;
    /// auto-detected overtime without a matching pre-approval is recorded as UNAPPROVED and excluded
    /// from payroll until HR reviews it. Default false (most tenants use auto-detection, §10).
    /// </summary>
    public bool RequireOvertimePreApproval { get; set; }

    // ── Monthly-summary policy (US-ATT-007) ────────────────────────────

    /// <summary>
    /// US-ATT-007 BR-5: when true, a worked day below the shift standard but at or above 50% of it is
    /// counted as 0.5 of a present day instead of a full present day. Default false — most tenants
    /// count any qualifying worked day as a full present day; half-day is opt-in per tenant policy.
    /// </summary>
    public bool HalfDayEnabled { get; set; }
}
