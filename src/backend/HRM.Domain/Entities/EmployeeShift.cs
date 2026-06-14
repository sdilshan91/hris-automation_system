namespace HRM.Domain.Entities;

/// <summary>
/// An effective-dated assignment of a <see cref="Shift"/> to an employee (US-ATT-005 §7, AC-2/AC-3,
/// FR-3/FR-4). Tenant-scoped via <see cref="BaseEntity.TenantId"/>. Maps to the "employee_shift" table.
///
/// BR-2: an employee has only one active assignment at any point in time. Activeness for a date D is
/// <c>EffectiveFrom &lt;= D AND (EffectiveTo IS NULL OR EffectiveTo &gt;= D)</c>. When a new assignment
/// becomes effective today/in the past, the previously open assignment is closed by setting its
/// <see cref="EffectiveTo"/> to the new assignment's <see cref="EffectiveFrom"/> minus one day.
/// </summary>
public sealed class EmployeeShift : BaseEntity
{
    /// <summary>FK to the assigned employee.</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>FK to the assigned shift.</summary>
    public Guid ShiftId { get; set; }

    /// <summary>First date (inclusive) the assignment is effective (BR-3, FR-4).</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Last date (inclusive) the assignment is effective; null means open/current (FR-4).</summary>
    public DateOnly? EffectiveTo { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public Employee? Employee { get; set; }
    public Shift? Shift { get; set; }
}
