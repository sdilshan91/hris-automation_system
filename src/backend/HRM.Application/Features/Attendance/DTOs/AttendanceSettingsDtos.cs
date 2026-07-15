namespace HRM.Application.Features.Attendance.DTOs;

// ════════════════════════════════════════════════════════════════════════
//  CAL-4b / US-ATT-011 AC-3 — Attendance policy configuration DTOs.
//  PINNED API contract (frontend + QA build against this EXACT shape).
//  Base: /api/v1/attendance, ApiResponse<T> envelope, all gated by
//  Attendance.ConfigurePolicy.
//   - GET/PUT  /settings                     → AttendanceSettingsDto   (the TENANT DEFAULT)
//   - GET      /settings/overrides           → IReadOnlyList<AttendanceSettingsDto>
//   - GET/PUT/DELETE /settings/overrides/{locationId} → AttendanceSettingsDto
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// A complete attendance policy for ONE scope (US-ATT-011 AC-3). Mirrors
/// <c>HRM.Domain.Entities.AttendanceSettings</c> field-for-field, with the same defaults.
///
/// <para><b>Which scope?</b> <see cref="LocationId"/> is READ-ONLY on the response and identifies the
/// scope: <c>null</c> = the TENANT DEFAULT policy; set = that Location's OVERRIDE. On a PUT the scope
/// comes from the ROUTE (<c>/settings</c> vs <c>/settings/overrides/{locationId}</c>), never from the
/// body — a <see cref="LocationId"/> in the request body is ignored.</para>
///
/// <para><b>Resolution is ROW-LEVEL, not per-field</b> (AC-3): an override row IS that Location's
/// complete policy. It does NOT merge field-by-field with the tenant-default row, and creating an
/// override does NOT copy the tenant row — the admin sends the complete policy. An override's employees
/// fall back to the tenant default only when the override row is deleted outright.</para>
///
/// <para>⚠ <b>PUT is a FULL REPLACE of that scope's policy (BUG-117 class).</b> Every field below is
/// applied as sent. A field OMITTED from the JSON body takes this record's default — it is NOT left at
/// its stored value — so an omitted field RESETS that setting (e.g. omitting
/// <c>weekendOvertimeMultiplier</c> resets it to 2.0, omitting <c>ipAllowlist</c> clears the list). The
/// client contract is therefore <b>GET-then-PUT</b>: read the current policy, mutate the fields you mean
/// to change, and send the whole object back. This is the known BUG-117 defect class and is documented,
/// not fixed, here.</para>
/// </summary>
public sealed record AttendanceSettingsDto
{
    /// <summary>
    /// READ-ONLY. The scope of this policy: null = the tenant default; set = this Location's override.
    /// Ignored on PUT — the scope is taken from the route.
    /// </summary>
    public Guid? LocationId { get; init; }

    /// <summary>
    /// READ-ONLY. The Location's name, populated on the overrides LIST so the UI need not re-resolve it.
    /// Null on the tenant-default policy.
    /// </summary>
    public string? LocationName { get; init; }

    // ── Clock-in enforcement (US-ATT-001 BR-2/BR-3/BR-6) ───────────────

    /// <summary>BR-2/AC-3: latitude+longitude are mandatory on clock-in.</summary>
    public bool RequireGeolocation { get; init; }

    /// <summary>FR-3: validate supplied coordinates against the allowed location.</summary>
    public bool GeoFenceEnabled { get; init; }

    /// <summary>FR-3: allowed-location latitude, [-90, 90]. Null when not configured.</summary>
    public decimal? GeoFenceLatitude { get; init; }

    /// <summary>FR-3: allowed-location longitude, [-180, 180]. Null when not configured.</summary>
    public decimal? GeoFenceLongitude { get; init; }

    /// <summary>FR-3: permitted radius in metres around the allowed location. Default 100.</summary>
    public int GeoFenceRadiusMeters { get; init; } = 100;

    /// <summary>BR-3/AC-5: the request source IP must be in <see cref="IpAllowlist"/>.</summary>
    public bool IpAllowlistEnabled { get; init; }

    /// <summary>FR-4: allowed source IPs — each an exact IP or a CIDR range (ISSUE-066).</summary>
    public List<string> IpAllowlist { get; init; } = new();

    /// <summary>BR-6: a selfie photo must be supplied before the clock-in is accepted.</summary>
    public bool RequirePhoto { get; init; }

    /// <summary>BR-4: minutes after shift start within which a clock-in is not marked late.</summary>
    public int GracePeriodMinutes { get; init; }

    // ── Work-hours calculation (US-ATT-002) ────────────────────────────

    /// <summary>FR-4/BR-3: standard scheduled work minutes for a full day. Default 480 (8 h).</summary>
    public int StandardWorkMinutes { get; init; } = 480;

    /// <summary>FR-4/BR-4: minimum net worked minutes before SHORT_DAY is flagged. Default 240 (4 h).</summary>
    public int MinimumWorkMinutes { get; init; } = 240;

    /// <summary>FR-3/BR-2: break minutes auto-deducted from gross worked time. Default 60.</summary>
    public int AutoBreakMinutes { get; init; } = 60;

    /// <summary>FR-3: gross minutes above which <see cref="AutoBreakMinutes"/> is deducted. Default 360 (6 h).</summary>
    public int AutoBreakThresholdMinutes { get; init; } = 360;

    /// <summary>BR-3: minutes beyond standard tolerated before the excess is overtime. Default 0.</summary>
    public int OvertimeThresholdMinutes { get; init; }

    // ── Regularization (US-ATT-003) ────────────────────────────────────

    /// <summary>FR-6/BR-2/AC-3: lookback window (calendar days) for a regularization request. Default 7.</summary>
    public int RegularizationLookbackDays { get; init; } = 7;

    // ── Overtime rules (US-ATT-006 FR-3) ───────────────────────────────

    /// <summary>BR-2: default overtime threshold (minutes) for overtime-record detection. Default 30.</summary>
    public int OvertimeMinimumThresholdMinutes { get; init; } = 30;

    /// <summary>BR-3: weekday overtime pay multiplier. Default 1.50.</summary>
    public decimal WeekdayOvertimeMultiplier { get; init; } = 1.5m;

    /// <summary>BR-3/BR-7: weekend (rest-day) overtime pay multiplier. Default 2.00.</summary>
    public decimal WeekendOvertimeMultiplier { get; init; } = 2.0m;

    /// <summary>BR-3/BR-7: public-holiday overtime pay multiplier. Default 2.50.</summary>
    public decimal HolidayOvertimeMultiplier { get; init; } = 2.5m;

    /// <summary>BR-4: maximum daily overtime (minutes). Default 240 (4 h).</summary>
    public int MaxDailyOvertimeMinutes { get; init; } = 240;

    /// <summary>BR-5: maximum weekly overtime (minutes). Default 1200 (20 h).</summary>
    public int MaxWeeklyOvertimeMinutes { get; init; } = 1200;

    /// <summary>FR-4/AC-2/BR-6: overtime requires a pre-approval request. Default false.</summary>
    public bool RequireOvertimePreApproval { get; init; }

    /// <summary>
    /// US-ATT-011 AC-5 / US-CHR-013: scale the overtime hourly BASE by the employee's FTE, so a 0.5-FTE
    /// employee on the same monthly basic earns a 2x hourly rate. Default false (the OT base ignores FTE).
    /// ⚠ Money-affecting and part of the FULL-REPLACE payload — omit it from a PUT and it resets to false.
    /// </summary>
    public bool FteScaledOvertimeBase { get; init; }

    // ── Monthly summary (US-ATT-007) ───────────────────────────────────

    /// <summary>BR-5: count a qualifying short day as 0.5 of a present day. Default false.</summary>
    public bool HalfDayEnabled { get; init; }

    // ── Absenteeism reporting (US-LV-011 BR-4) ─────────────────────────

    /// <summary>BR-4: average LOP days per month above which an employee is flagged. Default 3.</summary>
    public decimal AbsenteeismThresholdDays { get; init; } = 3m;
}
