namespace HRM.Domain.Enums;

/// <summary>
/// Department clearance decision recorded on an <see cref="Entities.OffboardingTaskInstance"/> (US-ONB-005
/// AC-3). Null (no decision recorded yet) is represented by the nullable clearance_status column on the
/// entity; this enum captures the two terminal decisions. Stored as a string column.
/// </summary>
public enum ClearanceStatus
{
    /// <summary>The department signed off — this clearance is cleared (green, AC-3/FR-4).</summary>
    Approved = 0,

    /// <summary>The department flagged outstanding issues (yellow/red, AC-3/FR-4). Blocks completion if mandatory.</summary>
    PendingIssues = 1,
}
