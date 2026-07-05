using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// An application submitted against a <see cref="Vacancy"/> (US-REC-002). Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> and the EF global query filter + <c>TenantInterceptor</c>
/// (this codebase enforces tenant isolation with EF query filters, not Postgres RLS — RLS/NFR-3 is a
/// separate deferred story; see the module note). Maps to the "applicant" table.
/// </summary>
public sealed class Applicant : BaseEntity
{
    /// <summary>FK to the <see cref="Vacancy"/> applied to (FR-1, required). Stored as a plain UUID column.</summary>
    public Guid VacancyId { get; set; }

    /// <summary>
    /// Human-readable, tenant+year sequential reference, e.g. "APP-2026-0001" (§7). Auto-generated on
    /// submit; unique per tenant. Mirrors the Vacancy.ReferenceNumber approach.
    /// </summary>
    public string ApplicationReferenceNumber { get; set; } = string.Empty;

    /// <summary>Applicant first name (FR-1, required, max 100 chars).</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Applicant last name (FR-1, required, max 100 chars).</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Applicant email (FR-1, required, validated). Uniquely identifies an applicant per vacancy
    /// (BR-1) — backed by a unique index on (tenant_id, vacancy_id, email).
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Applicant phone (FR-1, optional).</summary>
    public string? Phone { get; set; }

    /// <summary>Cover letter text (FR-1, optional, max 2000 chars). Plain text — not HTML.</summary>
    public string? CoverLetter { get; set; }

    /// <summary>
    /// Tenant-scoped storage key/path of the uploaded resume (FR-2). Set only after a clean virus scan
    /// (FR-3/NFR-4). Path convention: <c>recruitment/{vacancyId}/{applicantId}/{uuid-filename}</c>
    /// (the tenant id prefix is added by <c>IFileStorage</c>).
    /// </summary>
    public string ResumeStorageKey { get; set; } = string.Empty;

    /// <summary>Original (display) resume file name as uploaded (BR-3 — the stored key is UUID-renamed).</summary>
    public string ResumeFileName { get; set; } = string.Empty;

    /// <summary>Current pipeline stage (FR-6). Always <see cref="ApplicantStage.Applied"/> on submit.</summary>
    public ApplicantStage Stage { get; set; } = ApplicantStage.Applied;

    /// <summary>
    /// Structured rejection reason for the applicant's CURRENT state (US-REC-004 AC-4/FR-3). Set when the
    /// applicant is moved to <see cref="ApplicantStage.Rejected"/>; cleared when reactivated out of
    /// Rejected (BR-2). Null while the applicant is in any active stage. The full per-transition history
    /// lives on <see cref="ApplicantStageHistory"/>.
    /// </summary>
    public RejectionReason? RejectionReason { get; set; }

    /// <summary>Submission channel (§7). Public for anonymous, Internal for authenticated employees (BR-5).</summary>
    public ApplicationSource Source { get; set; } = ApplicationSource.Public;

    /// <summary>True when submitted by an authenticated existing employee (BR-5/FR-8).</summary>
    public bool IsInternal { get; set; }

    /// <summary>
    /// FK to the <see cref="Employee"/> record when this is an internal application (FR-8). Null for
    /// anonymous public applications. Stored as a nullable UUID column.
    /// </summary>
    public Guid? LinkedEmployeeId { get; set; }

    /// <summary>UTC instant the application was submitted (§7 applied_at).</summary>
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    // ── Conversion to employee (US-REC-010) ────────────────────────────

    /// <summary>
    /// FK to the <see cref="Employee"/> created when this applicant was converted (US-REC-010 FR-6).
    /// Null until the applicant is converted. Once set, the applicant cannot be converted again
    /// (FR-10/BR-2 — duplicate-conversion prevention). Plain nullable UUID column (no hard FK, mirroring
    /// the codebase's soft cross-module reference pattern, e.g. <see cref="LinkedEmployeeId"/>).
    /// </summary>
    public Guid? ConvertedToEmployeeId { get; set; }

    /// <summary>UTC instant the applicant was converted to an employee (US-REC-010 FR-6). Null until converted.</summary>
    public DateTime? ConvertedAt { get; set; }

    /// <summary>Id of the user who performed the conversion (US-REC-010 FR-6 converted_by). Null until converted.</summary>
    public Guid? ConvertedByUserId { get; set; }

    /// <summary>
    /// Optimistic concurrency token (ISSUE-109). On PostgreSQL this maps to the xmin system column via the
    /// Npgsql row-version convention (no schema DDL — xmin exists on every table); the InMemory test
    /// provider ignores it. Guards concurrent stage moves against lost updates. Mirrors
    /// <c>Goal.Version</c> / <c>Employee.RowVersion</c>.
    /// </summary>
    public uint RowVersion { get; set; }
}
