using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// An offboarding / exit-clearance process for a departing employee (US-ONB-005 AC-1). Created from an
/// offboarding <see cref="OnboardingChecklistTemplate"/> (Kind = Offboarding) or a built-in default clearance
/// set, anchored to the employee's last working day, owning a set of concrete
/// <see cref="OffboardingTaskInstance"/> rows whose due dates are calculated as LWD - offset_days (FR-2).
/// Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c>
/// (FR-8). Completion is irreversible (BR-6). Maps to the "offboarding_instance" table (snake_case).
/// </summary>
public sealed class OffboardingInstance : BaseEntity
{
    /// <summary>The departing employee this offboarding belongs to (AC-1). Cross-module ref to Employee.</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>The source offboarding template, if one was supplied (FR-1). Null = built-in default set.</summary>
    public Guid? TemplateId { get; set; }

    /// <summary>Snapshot of the template/source name at initiation time (survives template edits).</summary>
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>The employee's last working day (AC-1, FR-2). Due dates anchor to this date.</summary>
    public DateOnly LastWorkingDay { get; set; }

    /// <summary>Reason for departure (§7, BR-1).</summary>
    public OffboardingReason Reason { get; set; }

    /// <summary>Optional free-text notes (§7, max 2000).</summary>
    public string? Notes { get; set; }

    /// <summary>Lifecycle status (§7). New instances start <see cref="OffboardingStatus.InProgress"/>.</summary>
    public OffboardingStatus Status { get; set; } = OffboardingStatus.InProgress;

    /// <summary>The HR officer who initiated the offboarding (FR-8 audit / responsible-party resolution).</summary>
    public Guid? InitiatedByUserId { get; set; }

    /// <summary>When the offboarding was completed (AC-4). Null until completion.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>The user who completed the offboarding (AC-4 audit). Null until completion.</summary>
    public Guid? CompletedByUserId { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public List<OffboardingTaskInstance> Tasks { get; set; } = [];
}
