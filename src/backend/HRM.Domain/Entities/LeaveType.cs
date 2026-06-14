using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// Represents a configurable leave type within a tenant (US-LV-001).
/// Supports tenant-scoped CRUD, soft-delete, and rich policy configuration.
/// </summary>
public sealed class LeaveType : BaseEntity
{
    /// <summary>
    /// Leave type name, unique within a tenant (case-insensitive) (FR-2, BR-1).
    /// Example: "Annual Leave", "Sick Leave", "Maternity Leave".
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Short code for the leave type, e.g. "AL", "SL", "ML" (FR-2).
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// Hex color tag for UI display, e.g. "#4CAF50" (FR-2).
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Optional description of the leave type policy.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Annual entitlement in days (FR-2, BR-3).
    /// Zero is allowed for unpaid leave types.
    /// </summary>
    public decimal AnnualEntitlement { get; set; }

    /// <summary>
    /// Frequency at which entitlement is accrued (FR-2).
    /// </summary>
    public AccrualFrequency AccrualFrequency { get; set; } = AccrualFrequency.Upfront;

    /// <summary>
    /// Maximum days that can be carried forward to the next year (FR-2).
    /// Null means no carry-forward allowed.
    /// </summary>
    public decimal? CarryForwardLimit { get; set; }

    /// <summary>
    /// Number of months after which carried-forward days expire (FR-2).
    /// Null means carried-forward days never expire.
    /// </summary>
    public int? CarryForwardExpiryMonths { get; set; }

    /// <summary>
    /// Whether employees on probation can use this leave type (FR-2).
    /// </summary>
    public bool ProbationEligible { get; set; }

    /// <summary>
    /// Whether supporting documents are required for this leave type (FR-2, AC-5).
    /// </summary>
    public bool DocumentsRequired { get; set; }

    /// <summary>
    /// Day threshold above which documents are required (AC-5).
    /// Example: 2 means documents required when leave > 2 days.
    /// Only relevant when DocumentsRequired is true.
    /// </summary>
    public int? DocumentDayThreshold { get; set; }

    /// <summary>
    /// Whether unused days can be encashed (FR-2).
    /// </summary>
    public bool Encashable { get; set; }

    /// <summary>
    /// Maximum days that can be encashed (FR-2).
    /// Only relevant when Encashable is true.
    /// </summary>
    public decimal? MaxEncashDays { get; set; }

    /// <summary>
    /// Whether half-day leave is allowed for this type (FR-2).
    /// </summary>
    public bool HalfDayAllowed { get; set; }

    /// <summary>
    /// Whether hourly leave is allowed for this type (FR-2).
    /// </summary>
    public bool HourlyAllowed { get; set; }

    /// <summary>
    /// Gender applicability for this leave type (FR-2, BR-4).
    /// </summary>
    public LeaveTypeGender Gender { get; set; } = LeaveTypeGender.All;

    /// <summary>
    /// Maximum consecutive days allowed for a single leave request.
    /// Null means no limit.
    /// </summary>
    public int? MaxConsecutiveDays { get; set; }

    /// <summary>
    /// Whether negative balance is allowed for this leave type (FR-2).
    /// </summary>
    public bool NegativeBalanceAllowed { get; set; }

    /// <summary>
    /// Maximum negative balance allowed in days (FR-2).
    /// Only relevant when NegativeBalanceAllowed is true.
    /// </summary>
    public decimal? NegativeBalanceLimit { get; set; }

    /// <summary>
    /// Display order for UI presentation (FR-3).
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether this leave type is active (FR-5, AC-4).
    /// Inactive leave types are hidden from application forms but retained for historical reporting.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Special system category for this leave type (US-LV-011 FR-1). <see cref="LeaveTypeSystemCategory.None"/>
    /// for ordinary types. A non-None category (e.g. the LOP type) cannot be deleted/deactivated but can be
    /// renamed. Stored as a string.
    /// </summary>
    public LeaveTypeSystemCategory SystemCategory { get; set; } = LeaveTypeSystemCategory.None;
}
