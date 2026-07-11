namespace HRM.Application.Features.Benefits.DTOs;

/// <summary>An eligibility rule on a benefit plan (US-TRN-003 FR-1). Enum stored as its string name.</summary>
public sealed record EligibilityRuleDto
{
    public Guid Id { get; init; }
    public Guid BenefitPlanId { get; init; }
    public string Attribute { get; init; } = string.Empty;
    public string Operator { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

/// <summary>Request body for defining an eligibility rule on a plan (Benefits.Manage) (AC-1).</summary>
public sealed record CreateEligibilityRuleRequest
{
    public string Attribute { get; init; } = string.Empty;
    public string Operator { get; init; } = "==";
    public string Value { get; init; } = string.Empty;
}

/// <summary>A benefit plan the employee qualifies for right now (US-TRN-003 AC-2/AC-8).
/// <see cref="EnrollmentOpen"/> reflects whether the plan's open-enrollment window is currently open.</summary>
public sealed record EligiblePlanDto
{
    public Guid PlanId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public decimal? EmployeeCost { get; init; }
    public decimal? EmployerCost { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public bool EnrollmentOpen { get; init; }
}

/// <summary>Request body for enrolling into a plan (US-TRN-003 AC-3). A null <see cref="EmployeeId"/> means
/// "enroll me" (self-service, Benefits.View.Own); a non-null id enrolls another employee (Benefits.Manage).</summary>
public sealed record EnrollRequest
{
    public Guid PlanId { get; init; }
    public string CoverageLevel { get; init; } = "EmployeeOnly";
    public Guid? EmployeeId { get; init; }
}

/// <summary>A benefit enrollment (US-TRN-003 FR-2). Enum fields serialized as their string names.</summary>
public sealed record BenefitEnrollmentDto
{
    public Guid Id { get; init; }
    public Guid PlanId { get; init; }
    public string PlanName { get; init; } = string.Empty;
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string CoverageLevel { get; init; } = string.Empty;
    public DateOnly EffectiveDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public DateTime ElectedAt { get; init; }
}

/// <summary>Request body for terminating an enrollment (US-TRN-003 AC-7). Null <see cref="EndDate"/> = today.</summary>
public sealed record TerminateEnrollmentRequest
{
    public DateOnly? EndDate { get; init; }
}
