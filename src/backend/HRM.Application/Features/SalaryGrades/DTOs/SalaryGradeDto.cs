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
}
