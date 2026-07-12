namespace HRM.Domain.Entities;

/// <summary>
/// One immutable audit row recording a salary-structure assignment / revision for an employee
/// (US-PAY-002 FR-4/BR-3). Captures the before/after structure + CTC, the effective date, who changed it,
/// when, and the reason. Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter
/// + <c>TenantInterceptor</c>. Maps to the "salary_revision_history" table. Never updated or deleted in
/// normal operation (history is append-only).
/// </summary>
public sealed class SalaryRevisionHistory : BaseEntity, IAuditExempt
{
    /// <summary>FK to the employee whose compensation changed (BR-3, required).</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>The structure the employee held before this revision; null for the first-ever assignment (BR-3).</summary>
    public Guid? OldStructureId { get; set; }

    /// <summary>The structure assigned by this revision (BR-3, required).</summary>
    public Guid NewStructureId { get; set; }

    /// <summary>Annual CTC before this revision; null for the first-ever assignment (BR-3). numeric(18,2).</summary>
    public decimal? OldAnnualCtc { get; set; }

    /// <summary>Annual CTC declared by this revision (BR-3, required). numeric(18,2).</summary>
    public decimal NewAnnualCtc { get; set; }

    /// <summary>Date from which the revision takes effect (BR-3, required).</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Optional free-text reason for the change (BR-3).</summary>
    public string? Reason { get; set; }

    /// <summary>User id of the HR officer who made the change (BR-3, required).</summary>
    public Guid ChangedBy { get; set; }

    /// <summary>Timestamp the change was recorded (BR-3, required).</summary>
    public DateTime ChangedAt { get; set; }
}
