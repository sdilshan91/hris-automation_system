using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Domain.Performance;

/// <summary>
/// An employee's self-assessment against their goals for one appraisal cycle (US-PRF-002). Exactly one
/// per employee per cycle (enforced by a tenant-scoped unique index). Holds the overall status
/// (Draft / Submitted) and, once submitted, the weighted self-score (FR-4). Per-goal ratings live in the
/// child <see cref="SelfAssessmentItem"/> rows. Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the
/// EF global query filter + <c>TenantInterceptor</c> (NFR-2). Maps to the "self_assessment" table.
/// </summary>
public sealed class SelfAssessment : BaseEntity, IAuditExempt
{
    /// <summary>The appraisal cycle this self-assessment belongs to (FK, required).</summary>
    public Guid CycleId { get; set; }

    /// <summary>The employee who owns this self-assessment (FK to <see cref="Employee"/>, required).</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>Overall status. Defaults to Draft on first save (AC-3).</summary>
    public SelfAssessmentStatus Status { get; set; } = SelfAssessmentStatus.Draft;

    /// <summary>
    /// Weighted self-assessment score (FR-4), computed at submit from each item's selfRating and its goal's
    /// weight, normalized to the cycle's rating scale. Null until submitted. Stored as a decimal (max ~5.00).
    /// </summary>
    public decimal? WeightedSelfScore { get; set; }

    /// <summary>UTC submission timestamp (AC-2). Null until submitted.</summary>
    public DateTime? SubmittedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token (NFR — mirrors <c>Goal.Version</c>). Maps to the PostgreSQL xmin system
    /// column via the Npgsql row-version convention (no schema DDL); the InMemory test provider ignores it.
    /// </summary>
    public uint Version { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public AppraisalCycle? Cycle { get; set; }
    public Employee? Employee { get; set; }
    public List<SelfAssessmentItem> Items { get; set; } = [];
}
