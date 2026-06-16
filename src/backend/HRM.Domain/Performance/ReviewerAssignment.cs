using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Domain.Performance;

/// <summary>
/// A single 360-degree reviewer nomination for one reviewee, in one appraisal cycle (US-PRF-005 AC-1/FR-1/
/// FR-2). Records who reviews whom and in which category (Self / Manager / Peer / DirectReport), plus the
/// reviewer's completion status (Pending → Completed on submit, AC-3). Self + Manager are auto-assigned,
/// Peer + DirectReport are auto-suggested and manually adjustable. BR-2 (the reviewee can never be their own
/// Peer) and BR-3 (one feedback per reviewer per reviewee per cycle) are enforced by the service + a
/// tenant-scoped unique index. Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query
/// filter + <c>TenantInterceptor</c> (NFR-2). Maps to the "reviewer_assignment" table.
/// </summary>
public sealed class ReviewerAssignment : BaseEntity
{
    /// <summary>The appraisal cycle this assignment belongs to (FK, required).</summary>
    public Guid CycleId { get; set; }

    /// <summary>The employee being reviewed (FK to <see cref="Employee"/>, required).</summary>
    public Guid RevieweeEmployeeId { get; set; }

    /// <summary>The reviewing employee (FK to <see cref="Employee"/>, required).</summary>
    public Guid ReviewerEmployeeId { get; set; }

    /// <summary>The reviewer's perspective category (FR-1).</summary>
    public ReviewerCategory Category { get; set; }

    /// <summary>Completion status — Pending until the reviewer submits, then Completed (AC-3).</summary>
    public ReviewerAssignmentStatus Status { get; set; } = ReviewerAssignmentStatus.Pending;

    /// <summary>UTC timestamp the reviewer was notified of the assignment (AC-2). Null until notified.</summary>
    public DateTime? NotifiedAt { get; set; }

    /// <summary>UTC timestamp the reviewer submitted their feedback (AC-3). Null until Completed.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token (mirrors <c>Goal.Version</c> / <c>ManagerReview.Version</c>). Maps to the
    /// PostgreSQL xmin system column via the Npgsql row-version convention (no schema DDL); the InMemory test
    /// provider ignores it.
    /// </summary>
    public uint Version { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public AppraisalCycle? Cycle { get; set; }
    public Employee? Reviewee { get; set; }
    public Employee? Reviewer { get; set; }
}
