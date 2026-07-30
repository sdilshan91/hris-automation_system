namespace HRM.Application.Features.Performance.DTOs;

/// <summary>Input to apply/adjust a calibrated rating for one employee in a cycle (US-PRF-011 §3).</summary>
public sealed record ApplyCalibrationInput(
    Guid CycleId,
    Guid EmployeeId,
    decimal CalibratedScore,
    string Reason);

/// <summary>The result of applying a calibration — the original + newly-calibrated values (US-PRF-011 §3).</summary>
public sealed record CalibrationResultDto
{
    public Guid CalibrationId { get; init; }
    public Guid CycleId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid ManagerReviewId { get; init; }

    /// <summary>The review's untouched original final score (never overwritten by calibration).</summary>
    public decimal OriginalScore { get; init; }

    /// <summary>The calibrated value in effect before this action (null on the first round).</summary>
    public decimal? PreviousCalibratedScore { get; init; }

    /// <summary>The new calibrated score.</summary>
    public decimal CalibratedScore { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTime CalibratedAt { get; init; }
}

/// <summary>
/// The calibration cohort for one cycle (US-PRF-011 §2): the list an HR calibration session works from — each
/// employee's original rating, calibrated rating if any, reviewer, and department.
/// </summary>
public sealed record CalibrationCohortDto
{
    public Guid CycleId { get; init; }
    public string CycleName { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public int RatingScaleMax { get; init; }
    public IReadOnlyList<CalibrationCohortRowDto> Rows { get; init; } = [];
}

/// <summary>One row of the calibration cohort (US-PRF-011 §2).</summary>
public sealed record CalibrationCohortRowDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNo { get; init; } = string.Empty;
    public Guid DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;

    /// <summary>The employee's original manager-review final score (null if not yet scored).</summary>
    public decimal? OriginalScore { get; init; }

    /// <summary>The current calibrated score, if any calibration has been applied (else null).</summary>
    public decimal? CalibratedScore { get; init; }

    public Guid? ReviewerEmployeeId { get; init; }
    public string? ReviewerName { get; init; }
}
