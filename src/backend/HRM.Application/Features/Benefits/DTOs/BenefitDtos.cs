namespace HRM.Application.Features.Benefits.DTOs;

/// <summary>
/// A benefit plan (US-TRN-002). Enum fields are serialized as their string names to keep the FE contract stable.
/// </summary>
public sealed record BenefitPlanDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? CoverageDetails { get; init; }
    public decimal? EmployerCost { get; init; }
    public decimal? EmployeeCost { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public DateOnly? EnrollmentOpensAt { get; init; }
    public DateOnly? EnrollmentClosesAt { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>Request body for creating a benefit plan (creates it in <c>Draft</c>) (AC-1). Null
/// <see cref="Currency"/> resolves to the tenant's default currency.</summary>
public sealed record CreateBenefitPlanRequest
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = "Health";
    public string? Description { get; init; }
    public string? CoverageDetails { get; init; }
    public decimal? EmployerCost { get; init; }
    public decimal? EmployeeCost { get; init; }
    public string? Currency { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public DateOnly? EnrollmentOpensAt { get; init; }
    public DateOnly? EnrollmentClosesAt { get; init; }
}

/// <summary>Request body for updating a benefit plan's metadata/cost/coverage/dates/window (not status) (AC-4).</summary>
public sealed record UpdateBenefitPlanRequest
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = "Health";
    public string? Description { get; init; }
    public string? CoverageDetails { get; init; }
    public decimal? EmployerCost { get; init; }
    public decimal? EmployeeCost { get; init; }
    public string? Currency { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public DateOnly? EnrollmentOpensAt { get; init; }
    public DateOnly? EnrollmentClosesAt { get; init; }
}

/// <summary>Request body for a benefit-plan status transition (AC-2/AC-3/AC-6).</summary>
public sealed record ChangeBenefitPlanStatusRequest
{
    public string Status { get; init; } = string.Empty;
}
