namespace HRM.Application.Features.Attendance.DTOs;

// ════════════════════════════════════════════════════════════════════════
//  US-ATT-008 — Late arrival & early-departure tracking DTOs.
//  PINNED API contract (frontend + QA build against this EXACT shape).
//  Base: /api/v1/attendance, ApiResponse<T> envelope.
//   - GET/PUT  /late-policy            → LatePolicyDto                   (HR: Attendance.View.All)
//   - GET      /late-early/report      → LateEarlyReportResult          (team: Approve.Team / all: View.All)
//   - GET      /late-early/my-score    → LatenessScoreDto               (self: Attendance.CheckIn)
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Tenant late-arrival policy (US-ATT-008 §7, FR-4). Returned by GET /late-policy (defaults when none
/// configured) and accepted by PUT /late-policy (HR upsert).
/// </summary>
public sealed record LatePolicyDto
{
    /// <summary>Number of lates within <see cref="Period"/> that triggers the deduction (BR-4).</summary>
    public int ThresholdCount { get; init; } = 3;

    /// <summary>Days of pay deducted once the threshold is met, e.g. 0.5 (BR-4).</summary>
    public decimal DeductionDays { get; init; } = 0.5m;

    /// <summary>"MONTHLY" | "QUARTERLY".</summary>
    public string Period { get; init; } = "MONTHLY";

    /// <summary>FR-5: send an in-app notification on each late (delivery DEFERRED — US-NTF).</summary>
    public bool NotificationOnLate { get; init; }

    /// <summary>FR-7: lates per month above which HR is escalated (chronic).</summary>
    public int ChronicThreshold { get; init; } = 5;

    /// <summary>Whether the deduction rule is enforced (deductions are optional, §10).</summary>
    public bool IsActive { get; init; }
}

/// <summary>
/// One employee row in the late/early-departure report (US-ATT-008 AC-5/FR-6).
/// </summary>
public sealed record LateEarlyRowDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string? DepartmentName { get; init; }
    public int LateCount { get; init; }
    public int TotalLateMinutes { get; init; }
    public int EarlyDepartureCount { get; init; }
    public int TotalEarlyMinutes { get; init; }

    /// <summary>FR-7: lateCount exceeds the tenant chronic threshold for the period.</summary>
    public bool IsChronic { get; init; }
}

/// <summary>
/// The late/early-departure report result for a date range (US-ATT-008 AC-5/FR-6).
/// </summary>
public sealed record LateEarlyReportResult
{
    /// <summary>Range start (inclusive), "yyyy-MM-dd".</summary>
    public string From { get; init; } = string.Empty;

    /// <summary>Range end (inclusive), "yyyy-MM-dd".</summary>
    public string To { get; init; } = string.Empty;

    public IReadOnlyList<LateEarlyRowDto> Rows { get; init; } = [];
}

/// <summary>
/// The acting employee's monthly lateness score for the self-service dashboard (US-ATT-008 §8 —
/// "X of N allowed lates used this month"). Returned by GET /late-early/my-score.
/// </summary>
public sealed record LatenessScoreDto
{
    /// <summary>The month, "yyyy-MM".</summary>
    public string YearMonth { get; init; } = string.Empty;

    public int LateCount { get; init; }

    /// <summary>The tenant chronic threshold ("N allowed lates"); 0 when no policy is configured.</summary>
    public int AllowedLates { get; init; }

    public int EarlyDepartureCount { get; init; }
}
