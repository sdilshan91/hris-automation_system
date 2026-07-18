using HRM.Domain.Enums;

namespace HRM.Application.Features.Employees.DTOs;

/// <summary>
/// Full DTO for a single employee (detail views).
/// </summary>
public sealed record EmployeeDto
{
    public Guid Id { get; init; }
    public string EmployeeNo { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public DateTime? DateOfBirth { get; init; }
    public string? Gender { get; init; }
    public DateTime DateOfJoining { get; init; }
    public Guid DepartmentId { get; init; }
    public string? DepartmentName { get; init; }
    public Guid JobTitleId { get; init; }
    public string? JobTitleName { get; init; }
    /// <summary>US-CHR-011 (ISSUE-218): the reporting manager FK. Null when the employee has no manager.</summary>
    public Guid? ReportsToEmployeeId { get; init; }
    /// <summary>US-CHR-011 (ISSUE-218): the reporting manager's full name, resolved from the Manager nav. Null when unassigned.</summary>
    public string? ManagerName { get; init; }
    public string EmploymentType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    /// <summary>US-CHR-013: full-time equivalent. 1.00 = full-time, 0.50 = half-time.</summary>
    public decimal Fte { get; init; } = 1.00m;
    /// <summary>US-CHR-013: work arrangement — OnSite (default), Hybrid or Remote.</summary>
    public string WorkArrangement { get; init; } = nameof(Domain.Enums.WorkArrangement.OnSite);
    public string? ProfilePhotoUrl { get; init; }
    public string? Location { get; init; }
    /// <summary>
    /// FK to the structured Location entity (US-CHR-007). Null when unassigned (BR-6).
    /// </summary>
    public Guid? LocationId { get; init; }
    /// <summary>
    /// Name of the assigned location, resolved from the Location entity (US-CHR-007).
    /// </summary>
    public string? LocationName { get; init; }
    public string? CustomFields { get; init; }
    public Guid? UserId { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    /// <summary>
    /// ISSUE-293: national identity number, PII — MASKED (last-4, e.g. <c>******7890</c>) in this DTO. Null when
    /// not captured. The full plaintext is served only via the audited reveal endpoint
    /// (GET .../employees/{id}/national-id, Employee.View.All-gated).
    /// </summary>
    public string? NationalId { get; init; }
}

/// <summary>
/// ISSUE-293: result of the audited national-ID reveal endpoint. Carries the FULL decrypted plaintext (or null
/// when not captured). Served only to Employee.View.All holders and always writes an
/// <c>Employee.NationalId.ViewSensitive</c> audit row (naming the field, never the value).
/// </summary>
public sealed record NationalIdRevealDto
{
    public string? NationalId { get; init; }
}

/// <summary>
/// Paginated list result for employee queries.
/// </summary>
public sealed record EmployeeListResult
{
    public IReadOnlyList<EmployeeDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

/// <summary>
/// Request body for creating an employee (US-CHR-001 AC-2).
/// </summary>
public sealed record CreateEmployeeRequest
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public DateTime? DateOfBirth { get; init; }
    public Gender? Gender { get; init; }
    public DateTime DateOfJoining { get; init; }
    public Guid DepartmentId { get; init; }
    public Guid JobTitleId { get; init; }
    public EmploymentType EmploymentType { get; init; }
    public EmployeeStatus? Status { get; init; }
    /// <summary>
    /// US-CHR-013: full-time equivalent, (0, 1.00] at 2 dp. Omitted → 1.00 (full-time), which keeps every
    /// existing client's create behaviour identical.
    /// </summary>
    public decimal? Fte { get; init; }
    /// <summary>
    /// US-CHR-013: work arrangement. Omitted → OnSite, i.e. today's fully-enforced geo-fence behaviour.
    /// </summary>
    public WorkArrangement? WorkArrangement { get; init; }
    public string? Location { get; init; }
    /// <summary>
    /// Optional FK to a structured Location entity (US-CHR-007 / BUG-113). Null when unassigned (BR-6).
    /// </summary>
    public Guid? LocationId { get; init; }
    public string? CustomFields { get; init; }
    public Guid? UserId { get; init; }
    /// <summary>
    /// ISSUE-293: national identity number (free-text, max 50, PII). Stored encrypted at rest. Optional at create.
    /// </summary>
    public string? NationalId { get; init; }
}
