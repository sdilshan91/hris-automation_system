namespace HRM.Application.Features.Payroll.DTOs;

// ════════════════════════════════════════════════════════════════════════
//  US-PAY-010 — Attendance & Leave data integration into payroll. DTOs for the
//  leave-encashment trigger (AC-3/FR-5) and the pre-payroll reconciliation report
//  (FR-7). Base: /api/v1/payroll, ApiResponse<T> envelope. Money is decimal numeric(18,2).
// ════════════════════════════════════════════════════════════════════════

/// <summary>Request body to trigger a leave encashment (US-PAY-010 AC-3/FR-5).</summary>
public sealed record LeaveEncashmentRequest
{
    /// <summary>The employee whose leave is being encashed.</summary>
    public Guid EmployeeId { get; init; }

    /// <summary>The leave type being encashed (BR-6 eligibility). Null = HR override with a manual day count.</summary>
    public Guid? LeaveTypeId { get; init; }

    /// <summary>Number of unused days eligible for encashment.</summary>
    public decimal EligibleDays { get; init; }

    /// <summary>Target pay-period month (1-12). The encashment is added to the next available run.</summary>
    public int PayMonth { get; init; }

    /// <summary>Target pay-period year.</summary>
    public int PayYear { get; init; }

    /// <summary>Whether the encashment is taxable (default true).</summary>
    public bool IsTaxable { get; init; } = true;
}

/// <summary>The result of a leave encashment (US-PAY-010 AC-3/FR-5).</summary>
public sealed record LeaveEncashmentResultDto
{
    public Guid AdjustmentId { get; init; }
    public Guid EmployeeId { get; init; }

    /// <summary>The encashed days actually applied (after any BR-6 MaxEncashDays cap).</summary>
    public decimal EncashedDays { get; init; }

    /// <summary>The daily rate used = monthly_basic / working_days.</summary>
    public decimal DailyRate { get; init; }

    /// <summary>The encashment amount = encashed_days * daily_rate.</summary>
    public decimal Amount { get; init; }

    /// <summary>The pay-period month the encashment adjustment was created for (after BR-7/BR-8 deferral).</summary>
    public int PayMonth { get; init; }

    /// <summary>The pay-period year the encashment adjustment was created for.</summary>
    public int PayYear { get; init; }

    /// <summary>True when the target period was deferred (the requested period's run was Finalized/Processing).</summary>
    public bool PeriodDeferred { get; init; }
}

/// <summary>
/// One employee's pre-payroll reconciliation row (US-PAY-010 FR-7): the attendance + leave inputs HR reviews
/// before initiating a run, so mismatches (absence with no leave record) are caught up front.
/// </summary>
public sealed record PrePayrollReconciliationRowDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeNo { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;

    /// <summary>Scheduled working days in the period (shift calendar).</summary>
    public int WorkingDays { get; init; }

    /// <summary>Present days including half-days.</summary>
    public decimal PresentDays { get; init; }

    /// <summary>Absent days.</summary>
    public decimal AbsentDays { get; init; }

    /// <summary>Approved-leave days in the period, broken down by leave-type name.</summary>
    public IReadOnlyDictionary<string, decimal> LeaveDaysByType { get; init; } = new Dictionary<string, decimal>();

    /// <summary>Total approved-leave days across all types.</summary>
    public decimal TotalLeaveDays { get; init; }

    /// <summary>Approved overtime hours (approved minutes / 60).</summary>
    public decimal OvertimeHours { get; init; }

    /// <summary>Calculated loss-of-pay days (already incorporates late-deduction + half-day, BR-2/BR-3).</summary>
    public decimal CalculatedLopDays { get; init; }
}

/// <summary>The pre-payroll reconciliation report for a period (US-PAY-010 FR-7), tenant-scoped.</summary>
public sealed record PrePayrollReconciliationDto
{
    /// <summary>The period, "yyyy-MM".</summary>
    public string Period { get; init; } = string.Empty;

    public int PayMonth { get; init; }
    public int PayYear { get; init; }

    /// <summary>True when the attendance period is locked/finalized (AC-4 banner driver).</summary>
    public bool AttendanceFinalized { get; init; }

    public IReadOnlyList<PrePayrollReconciliationRowDto> Rows { get; init; } = [];
}
