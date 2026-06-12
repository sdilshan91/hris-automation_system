using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// Stores a future-dated employee status change that should be applied on or after the effective date
/// by a daily Hangfire background job (US-CHR-009 BR-4).
/// Once applied, the record is marked IsApplied = true with the applied timestamp.
/// </summary>
public sealed class FutureDatedStatusChange : BaseEntity
{
    /// <summary>
    /// The employee whose status will change.
    /// </summary>
    public Guid EmployeeId { get; set; }

    /// <summary>
    /// The status the employee had when this future change was requested.
    /// </summary>
    public EmployeeStatus FromStatus { get; set; }

    /// <summary>
    /// The target status to apply on the effective date.
    /// </summary>
    public EmployeeStatus ToStatus { get; set; }

    /// <summary>
    /// Required reason for the status change.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// The date on which the status change should take effect.
    /// Must be in the future at the time of creation.
    /// </summary>
    public DateTime EffectiveDate { get; set; }

    /// <summary>
    /// The user who requested the change.
    /// </summary>
    public Guid RequestedByUserId { get; set; }

    /// <summary>
    /// Display name/email of the requester.
    /// </summary>
    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>
    /// Whether this change has been applied by the background job.
    /// </summary>
    public bool IsApplied { get; set; }

    /// <summary>
    /// Timestamp when the background job applied this change (null if not yet applied).
    /// </summary>
    public DateTime? AppliedAt { get; set; }

    /// <summary>
    /// Whether this pending change was cancelled before being applied.
    /// </summary>
    public bool IsCancelled { get; set; }

    // ── Navigation ─────────────────────────────────────────────────

    public Employee? Employee { get; set; }
}
