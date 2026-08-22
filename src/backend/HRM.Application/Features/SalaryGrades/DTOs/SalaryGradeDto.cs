namespace HRM.Application.Features.SalaryGrades.DTOs;

/// <summary>
/// DTO for a salary grade (Payroll domain, ISSUE-021).
/// </summary>
public sealed record SalaryGradeDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal MinAmount { get; init; }
    public decimal? MidAmount { get; init; }
    public decimal MaxAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; }

    /// <summary>
    /// How many job titles currently point at this grade (B5). Surfaced so the UI can warn before
    /// deactivating one that is in use: <c>JobTitleService.ValidateGradeAsync</c> requires a job title's
    /// grade to resolve to an <b>active</b> grade, so deactivating a referenced grade makes those job
    /// titles fail their next save — a consequence the person clicking the toggle cannot otherwise see.
    /// It is a warning, not a block: retiring a grade while titles still reference it is legitimate during
    /// a re-grade, and refusing it would leave no way out.
    /// </summary>
    public int ReferencingJobTitleCount { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// Request body for creating a salary grade (ISSUE-021).
/// </summary>
public sealed record CreateSalaryGradeRequest
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal MinAmount { get; init; }
    public decimal? MidAmount { get; init; }
    public decimal MaxAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? Description { get; init; }
}

/// <summary>
/// Request body for updating a salary grade (ISSUE-021).
/// </summary>
public sealed record UpdateSalaryGradeRequest
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal MinAmount { get; init; }
    public decimal? MidAmount { get; init; }
    public decimal MaxAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? Description { get; init; }

    /// <summary>
    /// Whether the grade is active (B5). Previously absent, which made the edit form's Active toggle a
    /// silent no-op — the user flipped it, saved, saw success, and nothing changed. It also means a grade
    /// can now be RE-activated: before this, <c>DELETE</c> was the only way to change the flag and there
    /// was no route back, so a mis-clicked deactivation was permanent.
    ///
    /// <para>
    /// <b>Nullable on purpose: null means "leave unchanged".</b> A non-nullable <c>bool</c> defaulting to
    /// true would make every caller that omits the field silently REACTIVATE a deactivated grade — every
    /// existing integration and test posting the old body shape would quietly undo deactivations. Absent
    /// and "set it to false" must not be the same request.
    /// </para>
    /// </summary>
    public bool? IsActive { get; init; }
}
