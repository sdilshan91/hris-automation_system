namespace HRM.Application.Features.Employees.DTOs;

/// <summary>
/// Comprehensive employee profile DTO (US-CHR-002 AC-1).
/// Returns all profile sections: personal, contact, emergency contacts,
/// employment details, employment history, and custom fields.
/// </summary>
public sealed record EmployeeProfileDto
{
    public Guid Id { get; init; }
    public string EmployeeNo { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? PersonalEmail { get; init; }
    public string? Phone { get; init; }
    public string? Address { get; init; }
    /// <summary>DF-38: city component of the address. Null when not captured.</summary>
    public string? City { get; init; }
    /// <summary>DF-38: state/province component of the address. Null when not captured.</summary>
    public string? State { get; init; }
    /// <summary>DF-38: postal/ZIP code component of the address. Null when not captured.</summary>
    public string? PostalCode { get; init; }
    /// <summary>DF-38: country component of the address. Null when not captured.</summary>
    public string? Country { get; init; }
    public DateTime? DateOfBirth { get; init; }
    public string? Gender { get; init; }
    public DateTime DateOfJoining { get; init; }
    public Guid DepartmentId { get; init; }
    public string? DepartmentName { get; init; }
    public Guid JobTitleId { get; init; }
    public string? JobTitleName { get; init; }
    /// <summary>US-CHR-011 (ISSUE-218): the reporting manager FK. Null when unassigned.</summary>
    public Guid? ReportsToEmployeeId { get; init; }
    /// <summary>US-CHR-011 (ISSUE-218): the reporting manager's full name. Null when unassigned.</summary>
    public string? ManagerName { get; init; }
    public Guid? LocationId { get; init; }
    public string? LocationName { get; init; }
    public string EmploymentType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    /// <summary>US-CHR-013: full-time equivalent. 1.00 = full-time, 0.50 = half-time.</summary>
    public decimal Fte { get; init; } = 1.00m;
    /// <summary>US-CHR-013: work arrangement — OnSite (default), Hybrid or Remote.</summary>
    public string WorkArrangement { get; init; } = nameof(Domain.Enums.WorkArrangement.OnSite);
    public string? ProfilePhotoUrl { get; init; }
    public string? CustomFields { get; init; }
    public Guid? UserId { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    /// <summary>
    /// ISSUE-293: national identity number, PII — MASKED (last-4, e.g. <c>******7890</c>). Null when not captured.
    /// The full plaintext is served only via the audited reveal endpoint (Employee.View.All-gated).
    /// </summary>
    public string? NationalId { get; init; }

    /// <summary>
    /// Optimistic concurrency token (PostgreSQL xmin). Clients must send this
    /// back on PATCH requests; a stale value triggers 409 Conflict.
    /// </summary>
    public uint RowVersion { get; init; }

    /// <summary>
    /// Emergency contacts for this employee.
    /// </summary>
    public IReadOnlyList<EmergencyContactDto> EmergencyContacts { get; init; } = [];

    /// <summary>
    /// Employment history timeline entries, ordered by effective date descending.
    /// </summary>
    public IReadOnlyList<EmploymentHistoryDto> EmploymentHistory { get; init; } = [];

    /// <summary>
    /// Education history entries (DF-39).
    /// </summary>
    public IReadOnlyList<EducationDto> Education { get; init; } = [];

    /// <summary>
    /// Prior work-history entries (DF-39).
    /// </summary>
    public IReadOnlyList<WorkHistoryDto> WorkHistory { get; init; } = [];

    /// <summary>
    /// Dependents / family members (DF-39).
    /// </summary>
    public IReadOnlyList<DependentDto> Dependents { get; init; } = [];
}

/// <summary>
/// DTO for an education history entry (DF-39).
/// </summary>
public sealed record EducationDto
{
    public Guid Id { get; init; }
    public string Institution { get; init; } = string.Empty;
    public string Degree { get; init; } = string.Empty;
    public string? FieldOfStudy { get; init; }
    public string? StartYear { get; init; }
    public string? EndYear { get; init; }
}

/// <summary>
/// DTO for a work-history entry (DF-39).
/// </summary>
public sealed record WorkHistoryDto
{
    public Guid Id { get; init; }
    public string Company { get; init; } = string.Empty;
    public string Position { get; init; } = string.Empty;
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// DTO for a dependent entry (DF-39).
/// </summary>
public sealed record DependentDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Relationship { get; init; } = string.Empty;
    public DateOnly? DateOfBirth { get; init; }
}

/// <summary>
/// DTO for an emergency contact entry.
/// </summary>
public sealed record EmergencyContactDto
{
    public Guid Id { get; init; }
    public string ContactName { get; init; } = string.Empty;
    public string Relationship { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? AlternatePhone { get; init; }
    public string? Email { get; init; }
    public bool IsPrimary { get; init; }
}

/// <summary>
/// DTO for an employment history timeline entry.
/// </summary>
public sealed record EmploymentHistoryDto
{
    public Guid Id { get; init; }
    public string ChangeType { get; init; } = string.Empty;
    public string? PreviousValue { get; init; }
    public string NewValue { get; init; } = string.Empty;
    public Guid? PreviousReferenceId { get; init; }
    public Guid? NewReferenceId { get; init; }
    public DateTime EffectiveDate { get; init; }
    public string? Reason { get; init; }
    public string ChangedBy { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}
