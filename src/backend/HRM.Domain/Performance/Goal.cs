using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Domain.Performance;

/// <summary>
/// A performance goal / KPI assigned to an employee for an appraisal cycle (US-PRF-001). Set by the
/// employee's direct reporting manager (or HR) during the cycle's goal-setting window. Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c>.
/// Maps to the "goal" table.
/// </summary>
public sealed class Goal : BaseEntity, IAuditExempt
{
    /// <summary>The appraisal cycle this goal belongs to (FK, required).</summary>
    public Guid CycleId { get; set; }

    /// <summary>The employee this goal is assigned to (FK to <see cref="Employee"/>, required).</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>Goal title (FR-2, required, max 200 chars).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Goal description / detail (FR-2, optional, max 2000 chars).</summary>
    public string? Description { get; set; }

    /// <summary>Category — KPI / Competency / Project (FR-2, required).</summary>
    public GoalCategory Category { get; set; } = GoalCategory.Kpi;

    /// <summary>
    /// Weight as a whole-number percentage (FR-2). 1-100, in increments of 5% (BR-3). Per-employee
    /// per-cycle weights must sum to exactly 100% (FR-3/AC-3) — enforced at save time.
    /// </summary>
    public int Weight { get; set; }

    /// <summary>Target value the goal is measured against (FR-2, required, free-text e.g. "95%", "12").</summary>
    public string TargetValue { get; set; } = string.Empty;

    /// <summary>Unit of measurement (FR-2, required, e.g. "%", "tickets", "days").</summary>
    public string MeasurementUnit { get; set; } = string.Empty;

    /// <summary>Due date for the goal (FR-2, required). Date only — no time component.</summary>
    public DateOnly DueDate { get; set; }

    /// <summary>
    /// Optional parent goal for <b>alignment</b> (FR-4): links this employee goal to
    /// <b>another employee's goal</b>, typically the manager's own. Stored as a nullable self-FK
    /// (same tenant via global filter).
    /// <para>
    /// This is NOT an org/department cascade. <see cref="EmployeeId"/> is non-nullable, so a
    /// department- or company-owned objective cannot be represented and the parent of a goal is
    /// always another employee's goal. The org→department→individual tier is <b>withdrawn</b>
    /// (ADR-2026-08-11-goal-ownership-stays-individual), not deferred.
    /// </para>
    /// </summary>
    public Guid? ParentGoalId { get; set; }

    /// <summary>Current lifecycle status (AC-4). Defaults to Draft on create.</summary>
    public GoalStatus Status { get; set; } = GoalStatus.Draft;

    /// <summary>
    /// ISSUE-142 (US-PRF-009 TC-009-04): the UTC instant this goal was last nudged as a stale goal. The daily
    /// sweep skips a goal already nudged on the current date, so a same-day re-run / retry never double-notifies.
    /// Null = never nudged.
    /// </summary>
    public DateTime? LastStaleNudgeAtUtc { get; set; }

    /// <summary>
    /// Optimistic concurrency token (NFR-4). On PostgreSQL this maps to the xmin system column via the
    /// Npgsql row-version convention (no schema DDL); the InMemory test provider ignores it. Mirrors the
    /// <c>LeaveRequest.Version</c> pattern.
    /// </summary>
    public uint Version { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public AppraisalCycle? Cycle { get; set; }
    public Employee? Employee { get; set; }
    public Goal? ParentGoal { get; set; }
}
