namespace HRM.Application.Features.Payroll.DTOs;

// ── Breakdown / preview ──────────────────────────────────────────────────

/// <summary>One resolved component line in a CTC breakdown (US-PAY-002 FR-3).</summary>
public sealed record CtcComponentLineDto
{
    public Guid SalaryComponentId { get; init; }
    public string ComponentName { get; init; } = string.Empty;
    public string ComponentCode { get; init; } = string.Empty;
    public string ComponentType { get; init; } = string.Empty;
    public decimal AnnualAmount { get; init; }
    public decimal MonthlyAmount { get; init; }
    public bool IsOverride { get; init; }
    public int ProcessingOrder { get; init; }
}

/// <summary>CTC breakdown: component lines + totals (US-PAY-002 FR-3, used by the preview + assignment response).</summary>
public sealed record CtcBreakdownDto
{
    public Guid SalaryStructureId { get; init; }
    public decimal AnnualCtc { get; init; }
    public decimal TotalAnnualEarnings { get; init; }
    public decimal TotalMonthlyEarnings { get; init; }
    /// <summary>True when |TotalAnnualEarnings − AnnualCtc| is within the ±1 FR-6 tolerance (drives the FE preview indicator).</summary>
    public bool Balanced { get; init; }
    public IReadOnlyList<CtcComponentLineDto> Components { get; init; } = [];
}

// ── Current compensation + history ───────────────────────────────────────

/// <summary>An employee's current compensation snapshot (US-PAY-002 FR-4 read side).</summary>
public sealed record EmployeeCompensationDto
{
    public Guid EmployeeId { get; init; }
    public Guid SalaryStructureId { get; init; }
    public string SalaryStructureName { get; init; } = string.Empty;
    public decimal AnnualCtc { get; init; }
    public decimal MonthlyCtc { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public IReadOnlyList<CtcComponentLineDto> Components { get; init; } = [];
}

/// <summary>One salary-revision history entry for the timeline view (US-PAY-002 FR-4/BR-3).</summary>
public sealed record SalaryRevisionDto
{
    public Guid Id { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid? OldStructureId { get; init; }
    public Guid NewStructureId { get; init; }
    public decimal? OldAnnualCtc { get; init; }
    public decimal NewAnnualCtc { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public string? Reason { get; init; }
    public Guid ChangedBy { get; init; }
    public DateTime ChangedAt { get; init; }
}

// ── Request bodies ───────────────────────────────────────────────────────

/// <summary>A single per-component override in an assignment request (US-PAY-002 AC-3).</summary>
public sealed record SalaryComponentOverrideDto
{
    public Guid SalaryComponentId { get; init; }
    /// <summary>The fixed ANNUAL amount to use for this component instead of the calculated value.</summary>
    public decimal AnnualAmount { get; init; }
}

/// <summary>Request body for previewing a CTC breakdown before confirming (US-PAY-002 FR-3).</summary>
public sealed record PreviewCtcRequest
{
    public Guid SalaryStructureId { get; init; }
    public decimal AnnualCtc { get; init; }
    public IReadOnlyList<SalaryComponentOverrideDto> Overrides { get; init; } = [];
}

/// <summary>Request body for assigning a salary structure to an employee (US-PAY-002 FR-1/AC-1/AC-3).</summary>
public sealed record AssignSalaryStructureRequest
{
    public Guid EmployeeId { get; init; }
    public Guid SalaryStructureId { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public decimal AnnualCtc { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<SalaryComponentOverrideDto> Overrides { get; init; } = [];
}

/// <summary>One employee entry in a bulk-assignment request (US-PAY-002 AC-4/FR-5).</summary>
public sealed record BulkAssignEntryDto
{
    public Guid EmployeeId { get; init; }
    public decimal AnnualCtc { get; init; }
    public IReadOnlyList<SalaryComponentOverrideDto> Overrides { get; init; } = [];
}

/// <summary>Request body for bulk-assigning one salary structure to many employees (US-PAY-002 AC-4/FR-5).</summary>
public sealed record BulkAssignSalaryStructureRequest
{
    public Guid SalaryStructureId { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<BulkAssignEntryDto> Employees { get; init; } = [];
}

/// <summary>One per-employee outcome of a bulk assignment (US-PAY-002 AC-4 progress indicator).</summary>
public sealed record BulkAssignResultItemDto
{
    public Guid EmployeeId { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
}

/// <summary>Aggregate result of a bulk assignment (US-PAY-002 AC-4).</summary>
public sealed record BulkAssignResultDto
{
    public int TotalRequested { get; init; }
    public int SucceededCount { get; init; }
    public int FailedCount { get; init; }
    public IReadOnlyList<BulkAssignResultItemDto> Results { get; init; } = [];
}
