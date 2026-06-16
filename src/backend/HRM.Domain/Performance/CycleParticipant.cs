using HRM.Domain.Entities;

namespace HRM.Domain.Performance;

/// <summary>
/// An employee enrolled in an appraisal cycle (US-PRF-004 FR-3). The participant set is resolved ONCE at
/// cycle creation from the chosen scope (all / departments / grades / custom list) and snapshotted as rows
/// here. BR-4 (an employee may not be in two ACTIVE cycles of the same type) is enforced against these rows.
/// Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c>.
/// Maps to the "cycle_participant" table.
/// </summary>
public sealed class CycleParticipant : BaseEntity
{
    /// <summary>The cycle this participant belongs to (FK, required).</summary>
    public Guid CycleId { get; set; }

    /// <summary>The participating employee (FK to <see cref="Employee"/>, required).</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>Navigation back to the owning cycle.</summary>
    public AppraisalCycle? Cycle { get; set; }
}
