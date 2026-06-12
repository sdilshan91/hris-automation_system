using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// Represents an employee within a tenant's organization.
/// Tenant-scoped via BaseEntity.TenantId. Supports soft-delete (IsDeleted).
/// US-CHR-001: Add New Employee with Personal Information.
/// US-CHR-002: View and Edit Employee Profile (extended with address, personal email,
///   emergency contacts, employment history, concurrency token).
/// </summary>
public sealed class Employee : BaseEntity
{
    /// <summary>
    /// Auto-generated employee number, unique per tenant (FR-2, BR-1).
    /// Pattern default: "EMP-0001".
    /// </summary>
    public string EmployeeNo { get; set; } = string.Empty;

    /// <summary>
    /// Employee first name (required, max 100 chars).
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Employee last name (required, max 100 chars).
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Employee email, unique within a tenant (FR-3, BR-2).
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Personal email, editable by Employee role (US-CHR-002 AC-4).
    /// </summary>
    public string? PersonalEmail { get; set; }

    /// <summary>
    /// Phone number, optional. E.164 format preferred.
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Residential/mailing address, editable by Employee role (US-CHR-002 AC-4).
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Date of birth, optional. Must be in the past, age >= 16.
    /// </summary>
    public DateTime? DateOfBirth { get; set; }

    /// <summary>
    /// Gender, optional.
    /// </summary>
    public Gender? Gender { get; set; }

    /// <summary>
    /// Date of joining (required). Cannot be > 90 days in the future (BR-4).
    /// </summary>
    public DateTime DateOfJoining { get; set; }

    /// <summary>
    /// FK to Department (required). Must be an active department in the same tenant.
    /// </summary>
    public Guid DepartmentId { get; set; }

    /// <summary>
    /// FK to JobTitle (required). Must be an active job title in the same tenant.
    /// </summary>
    public Guid JobTitleId { get; set; }

    /// <summary>
    /// Employment type: Full-Time, Part-Time, Contract, Intern (required).
    /// </summary>
    public EmploymentType EmploymentType { get; set; }

    /// <summary>
    /// Employee status. Default: Active. Can be set to Probation on creation (BR-3).
    /// </summary>
    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;

    /// <summary>
    /// URL to stored profile photo (FR-6, AC-4).
    /// </summary>
    public string? ProfilePhotoUrl { get; set; }

    /// <summary>
    /// Tenant-configured custom field values stored as JSONB (FR-9, AC-6).
    /// TODO(US-CHR-012): Validate against tenant custom-field configuration when it exists.
    /// </summary>
    public string? CustomFields { get; set; }

    /// <summary>
    /// Optional FK to User for portal access (FR-8).
    /// Null means no login account linked.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Work location (free-text, e.g. "New York", "Remote").
    /// FK to a Location entity is deferred to US-CHR-007; for now this is a simple string.
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// Whether the employee is active (mirrors IsDeleted for soft-delete, but allows
    /// Active/Inactive/Terminated status transitions beyond just soft-delete).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// PostgreSQL xmin system column, used as an optimistic concurrency token (US-CHR-002 FR-4).
    /// EF Core maps this via UseXminAsConcurrencyToken(). The property is populated on read
    /// and checked on SaveChanges to detect concurrent modifications.
    /// </summary>
    public uint RowVersion { get; set; }

    // ── Navigation ─────────────────────────────────────────────────

    /// <summary>
    /// Department the employee belongs to.
    /// </summary>
    public Department? Department { get; set; }

    /// <summary>
    /// Job title assigned to the employee.
    /// </summary>
    public JobTitle? JobTitle { get; set; }

    /// <summary>
    /// Linked user account for portal access (nullable).
    /// </summary>
    public User? User { get; set; }

    /// <summary>
    /// Emergency contacts for this employee (US-CHR-002).
    /// </summary>
    public ICollection<EmergencyContact> EmergencyContacts { get; set; } = new List<EmergencyContact>();

    /// <summary>
    /// Employment history timeline entries (US-CHR-002 FR-6).
    /// </summary>
    public ICollection<EmploymentHistory> EmploymentHistories { get; set; } = new List<EmploymentHistory>();
}
