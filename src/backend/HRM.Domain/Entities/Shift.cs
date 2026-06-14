namespace HRM.Domain.Entities;

/// <summary>
/// Supported shift types (US-ATT-005 FR-1).
/// </summary>
public static class ShiftType
{
    /// <summary>Fixed start/end every working day.</summary>
    public const string Single = "SINGLE";

    /// <summary>Cyclic pattern of other shifts over a fixed cycle length (FR-7).</summary>
    public const string Rotating = "ROTATING";

    /// <summary>No fixed start/end; only a minimum-hours target is enforced (BR-8).</summary>
    public const string Flexible = "FLEXIBLE";

    public static bool IsValid(string type) =>
        type is Single or Rotating or Flexible;
}

/// <summary>
/// A shift definition for a tenant (US-ATT-005 §7, FR-1/FR-2). Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> and the EF global query filter. Maps to the "shift" table.
///
/// Drives expected work hours, lateness/grace, break deduction and minimum-hours per US-ATT-002/008.
/// For ROTATING shifts the rotation pattern is stored in <see cref="RotationSteps"/> and the
/// applicable concrete shift for a given date is computed by the service (FR-7).
/// </summary>
public sealed class Shift : BaseEntity
{
    /// <summary>Display name, unique per tenant (AC-1, BR-1 — partial unique index excludes soft-deleted).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>One of <see cref="ShiftType"/> (FR-1).</summary>
    public string Type { get; set; } = ShiftType.Single;

    /// <summary>
    /// Shift start time (local wall-clock). Null for FLEXIBLE (BR-8). For ROTATING this is the
    /// "header" shift's own time; per-day resolution uses the referenced step shifts (FR-7).
    /// </summary>
    public TimeOnly? StartTime { get; set; }

    /// <summary>Shift end time. Null for FLEXIBLE. May be &lt; <see cref="StartTime"/> for night shifts (§10).</summary>
    public TimeOnly? EndTime { get; set; }

    /// <summary>Break minutes auto-deducted from worked time on clock-out (BR-5; see US-ATT-002).</summary>
    public int BreakDurationMinutes { get; set; }

    /// <summary>Minutes after <see cref="StartTime"/> before a clock-in is "late" (BR-4).</summary>
    public int GracePeriodMinutes { get; set; }

    /// <summary>Required total hours for FLEXIBLE shifts (BR-8). Null/irrelevant for SINGLE/ROTATING headers.</summary>
    public decimal? MinimumHours { get; set; }

    /// <summary>Working day numbers 1=Mon..7=Sun (FR-2, BR-6). Stored as a PostgreSQL integer[].</summary>
    public List<int> WorkingDays { get; set; } = new();

    /// <summary>The tenant default shift used when an employee has no explicit assignment (FR-5, BR-1).</summary>
    public bool IsDefault { get; set; }

    /// <summary>Soft deactivation (FR-2 "is_active").</summary>
    public bool IsActive { get; set; } = true;

    // ── ROTATING only (FR-7) ───────────────────────────────────────────

    /// <summary>Total cycle length in days for a ROTATING shift (e.g. 14 for a 2-week A/B rotation).</summary>
    public int? RotationCycleLengthDays { get; set; }

    /// <summary>Anchor date the rotation cycle is measured from (FR-7). The day-index is computed modulo cycle length.</summary>
    public DateOnly? RotationReferenceStartDate { get; set; }

    /// <summary>Ordered steps composing the rotation (FR-7). Empty for non-ROTATING shifts.</summary>
    public List<ShiftRotationStep> RotationSteps { get; set; } = new();
}

/// <summary>
/// One step in a ROTATING shift's cycle (US-ATT-005 FR-7/AC-5). Each step points at a concrete
/// (SINGLE) shift that applies for <see cref="DurationDays"/> consecutive days within the cycle.
/// Tenant-scoped; maps to the "shift_rotation_step" table.
/// </summary>
public sealed class ShiftRotationStep : BaseEntity
{
    /// <summary>FK to the owning ROTATING shift.</summary>
    public Guid ShiftId { get; set; }

    /// <summary>0-based order of this step within the cycle (FR-7, §8 reorder).</summary>
    public int Order { get; set; }

    /// <summary>FK to the concrete shift that applies during this step.</summary>
    public Guid StepShiftId { get; set; }

    /// <summary>Number of consecutive days this step covers within the cycle.</summary>
    public int DurationDays { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public Shift? Shift { get; set; }
}
