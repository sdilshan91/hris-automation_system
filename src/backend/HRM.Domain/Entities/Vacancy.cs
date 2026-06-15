using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A job vacancy / open position within a tenant (US-REC-001). Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> and the EF global query filter + <c>TenantInterceptor</c>
/// (this codebase enforces tenant isolation with EF query filters, not Postgres RLS — see the
/// module note). Maps to the "vacancy" table.
/// </summary>
public sealed class Vacancy : BaseEntity
{
    /// <summary>
    /// Human-readable, tenant+year sequential reference, e.g. "VAC-2026-0001" (§7, §10). Auto-generated
    /// on create; unique per tenant.
    /// </summary>
    public string ReferenceNumber { get; set; } = string.Empty;

    /// <summary>Vacancy title (FR-1, required, max 200 chars).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Current lifecycle status (FR-2). Defaults to Draft on create (AC-1).</summary>
    public VacancyStatus Status { get; set; } = VacancyStatus.Draft;

    /// <summary>FK to the owning department (FR-1, required to publish — BR-2).</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>FK to the job title (FR-1, required to publish — BR-2).</summary>
    public Guid? JobTitleId { get; set; }

    /// <summary>FK to the work location (FR-1, optional).</summary>
    public Guid? LocationId { get; set; }

    /// <summary>FK to the hiring manager (an <see cref="Employee"/>) (FR-1, required to publish — BR-2).</summary>
    public Guid? HiringManagerId { get; set; }

    /// <summary>Employment type (FR-1, required).</summary>
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;

    /// <summary>Maximum number of positions to fill (FR-1, BR-4, integer &gt;= 1, required to publish).</summary>
    public int Headcount { get; set; } = 1;

    /// <summary>
    /// Number of positions filled so far (BR-4). Tracked as applicants are converted to employees
    /// (future recruitment stories); starts at 0.
    /// </summary>
    public int FilledCount { get; set; }

    /// <summary>Optional minimum of the salary range (FR-1).</summary>
    public decimal? SalaryMin { get; set; }

    /// <summary>Optional maximum of the salary range (FR-1).</summary>
    public decimal? SalaryMax { get; set; }

    /// <summary>ISO-4217 currency code for the salary range (FR-1). Null when no range is given.</summary>
    public string? SalaryCurrency { get; set; }

    /// <summary>Rich-text job description, stored as sanitized HTML (FR-1 required, NFR-4 XSS-safe).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Rich-text required qualifications, stored as sanitized HTML (FR-1 optional, NFR-4).</summary>
    public string? Qualifications { get; set; }

    /// <summary>Optional application deadline (FR-1). Date only — no time component.</summary>
    public DateOnly? ApplicationDeadline { get; set; }

    /// <summary>
    /// SEO-friendly URL slug for the public careers page (FR-5). Generated from the title on publish;
    /// unique per tenant. Null while the vacancy has never been published.
    /// </summary>
    public string? Slug { get; set; }

    /// <summary>
    /// Whether this vacancy may appear on the tenant's public careers page when Open (BR-5). Inherits
    /// the tenant toggle as a default but can be excluded per-vacancy. Effective public visibility also
    /// requires the tenant-level <c>PublicCareersEnabled</c> flag and an Open status (FR-4).
    /// </summary>
    public bool PublishToPublicCareers { get; set; } = true;

    /// <summary>UTC instant the vacancy was first published (Draft → Open). Null until first published.</summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>UTC instant the vacancy was closed (AC-5). Null unless closed.</summary>
    public DateTime? ClosedAt { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public Department? Department { get; set; }
    public JobTitle? JobTitle { get; set; }
    public Location? Location { get; set; }
    public Employee? HiringManager { get; set; }
}
