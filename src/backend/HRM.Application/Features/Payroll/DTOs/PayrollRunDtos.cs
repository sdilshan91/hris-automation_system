namespace HRM.Application.Features.Payroll.DTOs;

// ════════════════════════════════════════════════════════════════════════
//  US-PAY-003 — Payroll-run engine DTOs. Base: /api/v1/payroll, ApiResponse<T> envelope,
//  PascalCase enum strings (US-PLT-003). Money is decimal numeric(18,2).
// ════════════════════════════════════════════════════════════════════════

/// <summary>Request body to initiate a monthly payroll run (US-PAY-003 AC-1/FR-1).</summary>
public sealed record InitiatePayrollRunRequest
{
    /// <summary>Pay period month, 1-12 (FR-1).</summary>
    public int PayMonth { get; init; }

    /// <summary>Pay period year (FR-1).</summary>
    public int PayYear { get; init; }
}

/// <summary>The 202-Accepted payload returned by initiate (US-PAY-003 AC-1).</summary>
public sealed record PayrollRunAcceptedDto
{
    public Guid RunId { get; init; }
    public string Status { get; init; } = string.Empty;
    public int PayMonth { get; init; }
    public int PayYear { get; init; }
}

/// <summary>A payroll-run summary row for the runs list + detail (US-PAY-003 FR-8, §8 table view).</summary>
public sealed record PayrollRunDto
{
    public Guid Id { get; init; }
    public int PayMonth { get; init; }
    public int PayYear { get; init; }
    public string Status { get; init; } = string.Empty;
    public int TotalEmployees { get; init; }
    public int ProcessedEmployees { get; init; }
    public int SkippedEmployees { get; init; }
    public decimal TotalGross { get; init; }
    public decimal TotalDeductions { get; init; }
    public decimal TotalNet { get; init; }
    public decimal TotalStatutory { get; init; }
    public Guid InitiatedBy { get; init; }
    public DateTime InitiatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public Guid? ApprovedBy { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public DateTime? FinalizedAt { get; init; }
}

/// <summary>
/// The run summary + totals (US-PAY-003 FR-8). Includes the run log so HR can see skipped-employee
/// warnings (AC-6).
/// </summary>
public sealed record PayrollRunSummaryDto
{
    public PayrollRunDto Run { get; init; } = new();
    public string? RunLog { get; init; }
}

/// <summary>
/// Real-time-ish progress for a run (US-PAY-003 FR-6). SignalR is deferred — the FE polls this query for
/// processed/total while the run is in Processing (see the module note for the rationale).
/// </summary>
public sealed record PayrollRunProgressDto
{
    public Guid RunId { get; init; }
    public string Status { get; init; } = string.Empty;
    public int TotalEmployees { get; init; }
    public int ProcessedEmployees { get; init; }
    public int SkippedEmployees { get; init; }

    /// <summary>True once the run has left Queued/Processing (ReviewPending/Approved/Finalized/Cancelled).</summary>
    public bool IsComplete { get; init; }
}
