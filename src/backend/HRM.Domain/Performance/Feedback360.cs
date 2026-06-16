using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Domain.Performance;

/// <summary>
/// A single submitted 360-degree feedback record: one reviewer's evaluation of one reviewee in one cycle
/// (US-PRF-005 AC-3/FR-4). Holds the reviewer category, an optional overall free-text comment and the
/// per-competency/goal ratings (the child <see cref="Feedback360Item"/> rows, each on the cycle's
/// tenant-configured rating scale). The anonymity flag is CAPTURED AT SUBMIT TIME (BR-5) so it can never be
/// retroactively changed for an already-submitted record — anonymity is enforced in the result projection
/// (NFR-3/FR-5), where the reviewer identity is omitted from the DTO when this flag is set. Exactly one per
/// reviewer per reviewee per cycle (BR-3, tenant-scoped unique index). Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c> (NFR-2). Maps
/// to the "feedback_360" table.
/// </summary>
public sealed class Feedback360 : BaseEntity
{
    /// <summary>The appraisal cycle this feedback belongs to (FK, required).</summary>
    public Guid CycleId { get; set; }

    /// <summary>The employee being reviewed (FK to <see cref="Employee"/>, required).</summary>
    public Guid RevieweeEmployeeId { get; set; }

    /// <summary>The reviewing employee (FK to <see cref="Employee"/>, required).</summary>
    public Guid ReviewerEmployeeId { get; set; }

    /// <summary>The reviewer's perspective category (FR-1) — denormalized from the assignment for aggregation.</summary>
    public ReviewerCategory Category { get; set; }

    /// <summary>
    /// Whether this feedback was submitted under anonymity (BR-5). Captured from the cycle's
    /// <c>IsAnonymousFeedback</c> setting at submit time so it is immutable for this record — when true the
    /// result projection omits the reviewer identity (NFR-3/FR-5).
    /// </summary>
    public bool IsAnonymous { get; set; }

    /// <summary>Optional overall free-text comment for the whole feedback (FR-4, max 5000 chars).</summary>
    public string? OverallComment { get; set; }

    /// <summary>UTC submission timestamp (AC-3).</summary>
    public DateTime SubmittedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token (mirrors <c>ManagerReview.Version</c>). Maps to the PostgreSQL xmin
    /// system column via the Npgsql row-version convention; the InMemory test provider ignores it.
    /// </summary>
    public uint Version { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public AppraisalCycle? Cycle { get; set; }
    public Employee? Reviewee { get; set; }
    public Employee? Reviewer { get; set; }
    public List<Feedback360Item> Items { get; set; } = [];
}
