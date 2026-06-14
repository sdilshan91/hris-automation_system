namespace HRM.Domain.Entities;

/// <summary>
/// A compulsory-leave (company shutdown) day that HR bulk-assigns to employees (US-LV-011 FR-6, §7).
/// One row per shutdown date; the per-employee leave_request rows reference it via
/// <see cref="LeaveRequest"/> records carrying the same leave type / date. Tenant-scoped via
/// BaseEntity.TenantId. Maps to the "compulsory_leave" table.
/// </summary>
public sealed class CompulsoryLeave : BaseEntity
{
    /// <summary>The shutdown date the leave applies to.</summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// FK to the leave type deducted for this shutdown day (BR-4: deducted from balance first,
    /// then LOP for employees with insufficient balance).
    /// </summary>
    public Guid LeaveTypeId { get; set; }

    /// <summary>Free-text reason (e.g. "Company-wide shutdown — year-end").</summary>
    public string? Reason { get; set; }

    /// <summary>Number of per-employee leave_request rows generated for this shutdown day.</summary>
    public int AssignedCount { get; set; }

    /// <summary>Of <see cref="AssignedCount"/>, how many fell back to LOP for insufficient balance (BR-4).</summary>
    public int LopCount { get; set; }

    // ── Navigation ─────────────────────────────────────────────────

    public LeaveType? LeaveType { get; set; }
}
