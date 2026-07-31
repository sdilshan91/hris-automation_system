namespace HRM.Application.Features.LeaveEntitlements.DTOs;

/// <summary>
/// DTO for a leave entitlement rule (US-LV-002).
/// </summary>
public sealed record LeaveEntitlementRuleDto
{
    public Guid Id { get; init; }
    public Guid LeaveTypeId { get; init; }
    public string LeaveTypeName { get; init; } = string.Empty;
    public Guid? DepartmentId { get; init; }
    public string? DepartmentName { get; init; }
    public Guid? JobTitleId { get; init; }
    public string? JobTitleName { get; init; }
    public Guid? JobLevelId { get; init; }
    public string? EmploymentType { get; init; }
    public int? TenureMinMonths { get; init; }
    public int? TenureMaxMonths { get; init; }
    public decimal EntitlementDays { get; init; }
    public int Priority { get; init; }
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// Request body for creating or updating a leave entitlement rule.
/// </summary>
public sealed record UpsertLeaveEntitlementRuleRequest
{
    public Guid LeaveTypeId { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? JobTitleId { get; init; }
    public Guid? JobLevelId { get; init; }
    public string? EmploymentType { get; init; }
    public int? TenureMinMonths { get; init; }
    public int? TenureMaxMonths { get; init; }
    public decimal EntitlementDays { get; init; }
    public int Priority { get; init; }
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
}

/// <summary>
/// Request body for bulk-creating leave entitlement rules (FR-4).
/// </summary>
public sealed record BulkCreateRulesRequest
{
    public IReadOnlyList<UpsertLeaveEntitlementRuleRequest> Rules { get; init; } = [];
}

/// <summary>
/// DTO for a per-employee leave entitlement override (AC-3).
/// </summary>
public sealed record LeaveEntitlementOverrideDto
{
    public Guid Id { get; init; }
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public Guid LeaveTypeId { get; init; }
    public string LeaveTypeName { get; init; } = string.Empty;
    public int LeaveYear { get; init; }
    public decimal EntitlementDays { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// Request body for creating or upserting a per-employee override.
/// </summary>
public sealed record UpsertLeaveEntitlementOverrideRequest
{
    public Guid EmployeeId { get; init; }
    public Guid LeaveTypeId { get; init; }
    public int LeaveYear { get; init; }
    public decimal EntitlementDays { get; init; }
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Result of computing effective entitlement for an employee.
/// </summary>
public sealed record EffectiveEntitlementDto
{
    public Guid EmployeeId { get; init; }
    public Guid LeaveTypeId { get; init; }
    public string LeaveTypeName { get; init; } = string.Empty;
    public int LeaveYear { get; init; }
    /// <summary>
    /// Full-year entitlement before pro-rata.
    /// </summary>
    public decimal BaseEntitlementDays { get; init; }
    /// <summary>
    /// Entitlement after pro-rata for mid-year joiners (BR-2).
    /// </summary>
    public decimal ProratedEntitlementDays { get; init; }
    /// <summary>
    /// Source of the entitlement: "override", "rule:{ruleId}", or "leave_type_default".
    /// </summary>
    public string Source { get; init; } = string.Empty;
    /// <summary>
    /// Current balance from the leave ledger.
    /// </summary>
    public decimal CurrentBalance { get; init; }
}

/// <summary>
/// BUG-291 exposure report (READ-ONLY). One row per (employee × leave type) that a legacy full-year accrual
/// over-credited relative to what its <see cref="EmployeeNo"/>'s configured <see cref="AccrualFrequency"/>
/// should have accrued as of the caller-supplied as-of date. Reports the affected population only — it changes
/// nothing (no ledger adjustment). Only Monthly/Quarterly types can appear; Yearly/Upfront credit the whole
/// year in a single period so they can never be over-credited and are excluded by construction.
/// </summary>
public sealed record AccrualOverCreditExposureRow
{
    public Guid EmployeeId { get; init; }
    public string EmployeeNo { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public Guid LeaveTypeId { get; init; }
    public string LeaveTypeName { get; init; } = string.Empty;
    public int LeaveYear { get; init; }
    /// <summary>The type's configured accrual frequency (Monthly or Quarterly for any row that appears).</summary>
    public string AccrualFrequency { get; init; } = string.Empty;
    /// <summary>What the legacy accrual actually granted (sum of the Accrual ledger rows for the year).</summary>
    public decimal CreditedDays { get; init; }
    /// <summary>Frequency-aware entitlement that SHOULD have accrued by the as-of date
    /// (proratedAnnual × elapsedPeriods / periodsPerYear).</summary>
    public decimal ShouldHaveAccruedDays { get; init; }
    /// <summary>The over-credit: <see cref="CreditedDays"/> − <see cref="ShouldHaveAccruedDays"/> (always &gt; 0).</summary>
    public decimal OverCreditedDays { get; init; }
    /// <summary>Whether the employee is still active (a terminated employee's over-credit reaches F&amp;F).</summary>
    public bool IsEmployeeActive { get; init; }
}

/// <summary>
/// BUG-291 exposure report envelope. Tenant-scoped (current tenant only, under the normal query filters).
/// </summary>
public sealed record AccrualOverCreditExposureReportDto
{
    public DateOnly AsOfDate { get; init; }
    public int LeaveYear { get; init; }
    public IReadOnlyList<AccrualOverCreditExposureRow> Rows { get; init; } = [];
}
