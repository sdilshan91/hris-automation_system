namespace HRM.Domain.Entities;

/// <summary>
/// Materialized per-employee monthly attendance summary (US-ATT-007 §7). One row per
/// (tenant, employee, year_month). Computed by the aggregation service from the source attendance
/// data (AttendanceLog / OvertimeRecord / AttendanceRegularization), reconciled against approved
/// leave (LeaveRequest), excluding holidays + weekly-offs, and persisted here so the summary page
/// and exports read it directly instead of recomputing every request.
///
/// CACHING NOTE (FR-8 deferred): the story specifies a Redis cache keyed
/// <c>att_summary:{tenant}:{year_month}:{employee}</c>. This codebase has no Redis; THIS TABLE IS
/// the materialized cache — computed summaries are upserted here, and the read endpoints serve from
/// it. Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter (NFR-3 RLS
/// is NOT used — same as the rest of the Attendance module). Maps to "attendance_monthly_summary".
/// </summary>
public sealed class AttendanceMonthlySummary : BaseEntity
{
    /// <summary>FK to the employee the summary belongs to (§7).</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>The summarized month in "yyyy-MM" form, e.g. "2026-05" (§7).</summary>
    public string YearMonth { get; set; } = string.Empty;

    /// <summary>Total present days, supports half-days (BR-1/BR-5). decimal(4,1).</summary>
    public decimal TotalPresentDays { get; set; }

    /// <summary>Total absent days — scheduled working days with no record and no leave (BR-2). decimal(4,1).</summary>
    public decimal TotalAbsentDays { get; set; }

    /// <summary>Count of late arrivals (clock-in after shift start + grace, FR-3). Computed here pending US-ATT-008.</summary>
    public int TotalLateCount { get; set; }

    /// <summary>Count of early departures (clock-out before shift end, FR-3). Computed here pending US-ATT-008.</summary>
    public int TotalEarlyDepartureCount { get; set; }

    /// <summary>Total net worked minutes across the month (NFR-5 minute-accurate).</summary>
    public int TotalWorkMinutes { get; set; }

    /// <summary>Total APPROVED overtime minutes across the month (FR-3 — approved only).</summary>
    public int TotalOvertimeMinutes { get; set; }

    /// <summary>Approved leave days in the month, from the Leave module (BR-6). decimal(4,1).</summary>
    public decimal TotalLeaveDays { get; set; }

    /// <summary>Public holidays falling within the month for the employee (BR-4).</summary>
    public int TotalHolidays { get; set; }

    /// <summary>Weekly-off days in the month per the employee's resolved shift working-days (BR-4).</summary>
    public int TotalWeeklyOffs { get; set; }

    /// <summary>Loss-of-Pay days: absent days not covered by any leave (BR-3). decimal(4,1).</summary>
    public decimal LopDays { get; set; }

    /// <summary>When the summary was last (re)computed (§7).</summary>
    public DateTime GeneratedAt { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public Employee? Employee { get; set; }
}
