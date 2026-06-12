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
    public DateTime? DateOfBirth { get; init; }
    public string? Gender { get; init; }
    public DateTime DateOfJoining { get; init; }
    public Guid DepartmentId { get; init; }
    public string? DepartmentName { get; init; }
    public Guid JobTitleId { get; init; }
    public string? JobTitleName { get; init; }
    public string EmploymentType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? ProfilePhotoUrl { get; init; }
    public string? CustomFields { get; init; }
    public Guid? UserId { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }

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
