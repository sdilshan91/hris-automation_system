namespace HRM.Application.Features.Attendance.DTOs;

// ════════════════════════════════════════════════════════════════════════
//  US-ATT-009 — Attendance ⇄ Payroll integration DTOs (ATTENDANCE SIDE only).
//  Pinned API contract (frontend + QA build against this exact shape).
//  Base: /api/v1/attendance, ApiResponse<T> envelope.
//
//  SCOPE BOUNDARY: the Payroll module (module 6) does not exist yet. AC-2 (LOP deduction =
//  salary/working_days * lop_days) and AC-3 (overtime_pay = hourly_rate * 1.5 * hours) are
//  PAYROLL-SIDE salary computations and are NOT implemented here — this contract exposes the
//  ATTENDANCE DATA (days/minutes) the payroll module will consume (FR-1/FR-2), plus the lock.
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// One per-employee attendance row consumed by the payroll module (US-ATT-009 FR-2). All
/// salary math is the payroll module's job; this row carries only the attendance inputs.
/// </summary>
public sealed record AttendancePayrollRowDto
{
    public Guid EmployeeId { get; init; }

    /// <summary>The payroll period, "yyyy-MM".</summary>
    public string Period { get; init; } = string.Empty;

    /// <summary>Scheduled working days in the period from the resolved shift/calendar (§7).</summary>
    public int TotalWorkingDays { get; init; }

    /// <summary>Present days including half-days (decimal).</summary>
    public decimal TotalPresentDays { get; init; }

    public decimal TotalAbsentDays { get; init; }

    /// <summary>
    /// Loss-of-pay days (FR-7): unexcused absences (absent − approved-leave-covered) PLUS the
    /// late-deduction days (BR-4). Approved-leave-covered absences do NOT count toward LOP.
    /// </summary>
    public decimal LopDays { get; init; }

    /// <summary>The late-deduction-days portion of <see cref="LopDays"/> (US-ATT-008 BR-4), surfaced
    /// separately so payroll/HR can see the breakdown.</summary>
    public decimal LateDeductionDays { get; init; }

    /// <summary>APPROVED overtime minutes only (FR-8 — pending/rejected excluded).</summary>
    public int ApprovedOvertimeMinutes { get; init; }

    /// <summary>Total net worked minutes in the period.</summary>
    public int TotalWorkMinutes { get; init; }

    /// <summary>
    /// Approved-overtime minutes broken down by multiplier rate (FR-2 jsonb), keyed by the multiplier
    /// (e.g. "1.5" → 600). The payroll module multiplies each bucket by the rate for overtime pay
    /// (AC-3 computation lives in payroll, not here).
    /// </summary>
    public IReadOnlyDictionary<string, int> OvertimeMultiplierDetails { get; init; }
        = new Dictionary<string, int>();
}

/// <summary>
/// The attendance-to-payroll data pull result for a period (US-ATT-009 FR-1/FR-2/NFR-1).
/// </summary>
public sealed record AttendancePayrollResult
{
    /// <summary>The payroll period, "yyyy-MM".</summary>
    public string Period { get; init; } = string.Empty;

    public IReadOnlyList<AttendancePayrollRowDto> Rows { get; init; } = [];
}

/// <summary>
/// The current attendance period lock for a month (US-ATT-009 §7) — drives the lock banner/status
/// and the lock/unlock actions. Mirrors the AttendancePeriodLock entity.
/// </summary>
public sealed record PeriodLockDto
{
    public Guid Id { get; init; }

    /// <summary>Inclusive first locked date, "yyyy-MM-dd".</summary>
    public DateOnly PeriodStart { get; init; }

    /// <summary>Inclusive last locked date, "yyyy-MM-dd".</summary>
    public DateOnly PeriodEnd { get; init; }

    public bool IsLocked { get; init; }

    public Guid? LockedBy { get; init; }
    public DateTime? LockedAt { get; init; }
    public Guid? UnlockedBy { get; init; }
    public DateTime? UnlockedAt { get; init; }
}

/// <summary>
/// Request body to lock an attendance period (US-ATT-009 AC-4/FR-3).
/// </summary>
public sealed record LockPeriodRequest
{
    /// <summary>Inclusive first locked date, "yyyy-MM-dd".</summary>
    public DateOnly PeriodStart { get; init; }

    /// <summary>Inclusive last locked date, "yyyy-MM-dd".</summary>
    public DateOnly PeriodEnd { get; init; }
}

/// <summary>
/// One reconciliation row (US-ATT-009 FR-5, ATTENDANCE SIDE). The payroll-input columns are DEFERRED
/// (the Payroll module does not exist) — this surfaces the attendance summary side only.
/// </summary>
public sealed record ReconciliationRowDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public decimal PresentDays { get; init; }
    public decimal LopDays { get; init; }
    public int ApprovedOvertimeMinutes { get; init; }
    public int TotalWorkMinutes { get; init; }
}

/// <summary>
/// Attendance-vs-payroll reconciliation result for a period (US-ATT-009 FR-5, attendance side).
/// </summary>
public sealed record ReconciliationResult
{
    /// <summary>The period, "yyyy-MM".</summary>
    public string Period { get; init; } = string.Empty;

    public IReadOnlyList<ReconciliationRowDto> Rows { get; init; } = [];
}
