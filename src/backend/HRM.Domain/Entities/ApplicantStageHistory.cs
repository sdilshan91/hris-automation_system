using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// An immutable record of a single pipeline stage transition for an <see cref="Applicant"/>
/// (US-REC-003 BR-5/AC-2). One row is written per stage move, capturing from-stage, to-stage, the
/// acting user, an optional reason (mandatory when moving to Rejected per BR-3, or on a backward move
/// per BR-4) and optional free-text notes. Powers the applicant detail timeline (AC-3/FR-7).
///
/// Maps to the "applicant_stage_history" table. Tenant-scoped via <see cref="BaseEntity.TenantId"/> and
/// the EF global query filter + <c>TenantInterceptor</c> (this codebase enforces tenant isolation with
/// EF query filters, not Postgres RLS — RLS/NFR-3 is a separate deferred story; see the module note).
/// </summary>
public sealed class ApplicantStageHistory : BaseEntity
{
    /// <summary>FK to the <see cref="Applicant"/> this transition belongs to (required). Plain UUID column.</summary>
    public Guid ApplicantId { get; set; }

    /// <summary>The stage the applicant moved FROM (BR-5).</summary>
    public ApplicantStage FromStage { get; set; }

    /// <summary>The stage the applicant moved TO (BR-5).</summary>
    public ApplicantStage ToStage { get; set; }

    /// <summary>
    /// FK (user id) of the recruiter/manager who performed the move (BR-5). Nullable so a system/seeded
    /// move (no authenticated user) is still recordable; the API path always populates it.
    /// </summary>
    public Guid? ChangedByUserId { get; set; }

    /// <summary>
    /// Reason for the transition. Mandatory when moving to Rejected (BR-3) or on a backward move (BR-4);
    /// optional otherwise. Stored as text.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>Optional free-text notes about the transition (BR-5). Stored as text.</summary>
    public string? Notes { get; set; }

    /// <summary>UTC instant the transition was recorded (BR-5 timestamp).</summary>
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation ─────────────────────────────────────────────────

    public Applicant? Applicant { get; set; }
}
