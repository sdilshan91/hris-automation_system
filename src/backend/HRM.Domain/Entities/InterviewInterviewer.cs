namespace HRM.Domain.Entities;

/// <summary>
/// Junction row linking an <see cref="Interview"/> to an interviewer (an <see cref="Employee"/>) —
/// US-REC-005 FR-1 (≥1 interviewer required). Tenant-scoped via <see cref="BaseEntity.TenantId"/> and the
/// EF global query filter + <c>TenantInterceptor</c>. Maps to the "interview_interviewer" table.
/// </summary>
public sealed class InterviewInterviewer : BaseEntity
{
    /// <summary>FK to the parent <see cref="Interview"/> (required). Plain UUID column.</summary>
    public Guid InterviewId { get; set; }

    /// <summary>
    /// FK to the interviewer's <see cref="Employee"/> record (required, BR-2 — must be an active employee
    /// in the tenant). Plain UUID column. A hard FK constraint is intentionally omitted to mirror the
    /// codebase pattern for cross-module references; tenant-scoped existence is validated in the service.
    /// </summary>
    public Guid EmployeeId { get; set; }

    // ── Navigation ─────────────────────────────────────────────────

    public Interview? Interview { get; set; }
}
