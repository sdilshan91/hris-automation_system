using HRM.Domain.Enums;

namespace HRM.Application.Features.Employees.DTOs;

/// <summary>
/// Request for updating an employee profile section (US-CHR-002 AC-2, AC-4, AC-5).
/// Only the section being edited is set; null sections are ignored.
/// The RowVersion is required for optimistic concurrency (FR-4).
/// </summary>
public sealed record UpdateEmployeeProfileRequest
{
    /// <summary>
    /// Optimistic concurrency token. Must match the current xmin value.
    /// </summary>
    public uint RowVersion { get; init; }

    /// <summary>
    /// Personal info section. Null means this section is not being updated.
    /// </summary>
    public PersonalInfoUpdate? PersonalInfo { get; init; }

    /// <summary>
    /// Contact section. Null means this section is not being updated.
    /// </summary>
    public ContactInfoUpdate? ContactInfo { get; init; }

    /// <summary>
    /// Employment section. Null means this section is not being updated.
    /// </summary>
    public EmploymentInfoUpdate? EmploymentInfo { get; init; }

    /// <summary>
    /// Emergency contacts section (full replace). Null means not being updated.
    /// </summary>
    public IReadOnlyList<EmergencyContactInput>? EmergencyContacts { get; init; }

    /// <summary>
    /// Custom fields JSONB. Null means not being updated.
    /// </summary>
    public string? CustomFields { get; init; }

    /// <summary>
    /// Sentinel to distinguish "don't update custom fields" (null) from
    /// "clear custom fields" (explicit empty string).
    /// </summary>
    public bool UpdateCustomFields { get; init; }
}

/// <summary>
/// Updatable personal info fields (US-CHR-002 AC-4: name, DOB, gender are
/// read-only for Employee role; read/write for HR Officer).
/// </summary>
public sealed record PersonalInfoUpdate
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public DateTime? DateOfBirth { get; init; }
    public Gender? Gender { get; init; }
    /// <summary>
    /// ISSUE-293: national identity number (free-text, max 50). PII, HR-writable only (same field-level
    /// permission as the rest of PersonalInfo — read-only for Employee role per AC-4). Null leaves the current
    /// value unchanged. Stored encrypted at rest; the audit interceptor redacts <c>national_id</c> via
    /// SensitiveFieldMasker, so the before/after audit JSON never carries the plaintext.
    /// </summary>
    public string? NationalId { get; init; }
}

/// <summary>
/// Updatable contact fields. Phone, PersonalEmail, and Address are
/// editable by both Employee and HR Officer roles (AC-4).
/// </summary>
public sealed record ContactInfoUpdate
{
    public string? Phone { get; init; }
    public string? PersonalEmail { get; init; }
    public string? Address { get; init; }
}

/// <summary>
/// Updatable employment fields. Only HR Officer can modify these (AC-5, BR-4).
/// Changes to Department/JobTitle/Status trigger EmploymentHistory entries (AC-6).
/// </summary>
public sealed record EmploymentInfoUpdate
{
    public Guid? DepartmentId { get; init; }
    public Guid? JobTitleId { get; init; }
    /// <summary>
    /// Optional FK to a structured Location entity (US-CHR-007 / BUG-113). When set and different
    /// from the current value, reassigns the employee's location and records an employment-history entry.
    /// A null value leaves the current location unchanged (mirrors DepartmentId/JobTitleId).
    /// </summary>
    public Guid? LocationId { get; init; }
    public EmploymentType? EmploymentType { get; init; }
    public EmployeeStatus? Status { get; init; }
    /// <summary>
    /// US-CHR-013: full-time equivalent, (0, 1.00] at 2 dp. Null leaves the current value unchanged
    /// (mirrors DepartmentId/JobTitleId). Changing it changes future leave accrual.
    /// </summary>
    public decimal? Fte { get; init; }
    /// <summary>
    /// US-CHR-013: work arrangement. Null leaves the current value unchanged. Setting Remote exempts the
    /// employee's clock-in from the geo-fence radius check (and nothing else).
    /// </summary>
    public WorkArrangement? WorkArrangement { get; init; }
    /// <summary>
    /// Effective date for the employment change. Defaults to today.
    /// </summary>
    public DateTime? EffectiveDate { get; init; }
    /// <summary>
    /// Optional reason/notes for the change.
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Input for an emergency contact entry (create or replace).
/// </summary>
public sealed record EmergencyContactInput
{
    /// <summary>
    /// If set, updates an existing contact; if null/empty, creates a new one.
    /// </summary>
    public Guid? Id { get; init; }
    public string ContactName { get; init; } = string.Empty;
    public string Relationship { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? AlternatePhone { get; init; }
    public string? Email { get; init; }
    public bool IsPrimary { get; init; }
}
