namespace HRM.Application.Features.LeaveTypes.DTOs;

/// <summary>
/// DTO for a leave type (US-LV-001).
/// </summary>
public sealed record LeaveTypeDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Code { get; init; }
    public string? Color { get; init; }
    public string? Description { get; init; }
    public decimal AnnualEntitlement { get; init; }
    public string AccrualFrequency { get; init; } = string.Empty;
    public decimal? CarryForwardLimit { get; init; }
    public int? CarryForwardExpiryMonths { get; init; }
    public bool ProbationEligible { get; init; }
    public bool DocumentsRequired { get; init; }
    public int? DocumentDayThreshold { get; init; }
    public bool Encashable { get; init; }
    public decimal? MaxEncashDays { get; init; }
    public bool HalfDayAllowed { get; init; }
    public bool HourlyAllowed { get; init; }
    public string Gender { get; init; } = "All";
    public int? MaxConsecutiveDays { get; init; }
    public bool NegativeBalanceAllowed { get; init; }
    public decimal? NegativeBalanceLimit { get; init; }
    public int DisplayOrder { get; init; }
    public bool IsActive { get; init; }
    /// <summary>
    /// System category: "None" for ordinary types, "LossOfPay" for the system LOP type (US-LV-011 FR-1).
    /// A non-None type cannot be deleted/deactivated but can be renamed.
    /// </summary>
    public string SystemCategory { get; init; } = "None";
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// Request body for creating a leave type (US-LV-001 AC-1).
/// </summary>
public sealed record CreateLeaveTypeRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Code { get; init; }
    public string? Color { get; init; }
    public string? Description { get; init; }
    public decimal AnnualEntitlement { get; init; }
    public string AccrualFrequency { get; init; } = "Upfront";
    public decimal? CarryForwardLimit { get; init; }
    public int? CarryForwardExpiryMonths { get; init; }
    public bool ProbationEligible { get; init; }
    public bool DocumentsRequired { get; init; }
    public int? DocumentDayThreshold { get; init; }
    public bool Encashable { get; init; }
    public decimal? MaxEncashDays { get; init; }
    public bool HalfDayAllowed { get; init; }
    public bool HourlyAllowed { get; init; }
    public string Gender { get; init; } = "All";
    public int? MaxConsecutiveDays { get; init; }
    public bool NegativeBalanceAllowed { get; init; }
    public decimal? NegativeBalanceLimit { get; init; }
    public int? DisplayOrder { get; init; }
}

/// <summary>
/// Request body for updating a leave type (US-LV-001 AC-2).
/// </summary>
public sealed record UpdateLeaveTypeRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Code { get; init; }
    public string? Color { get; init; }
    public string? Description { get; init; }
    public decimal AnnualEntitlement { get; init; }
    public string AccrualFrequency { get; init; } = "Upfront";
    public decimal? CarryForwardLimit { get; init; }
    public int? CarryForwardExpiryMonths { get; init; }
    public bool ProbationEligible { get; init; }
    public bool DocumentsRequired { get; init; }
    public int? DocumentDayThreshold { get; init; }
    public bool Encashable { get; init; }
    public decimal? MaxEncashDays { get; init; }
    public bool HalfDayAllowed { get; init; }
    public bool HourlyAllowed { get; init; }
    public string Gender { get; init; } = "All";
    public int? MaxConsecutiveDays { get; init; }
    public bool NegativeBalanceAllowed { get; init; }
    public decimal? NegativeBalanceLimit { get; init; }
}

/// <summary>
/// Request body for reordering leave types (US-LV-001 FR-3).
/// </summary>
public sealed record ReorderLeaveTypesRequest
{
    /// <summary>
    /// Ordered list of leave type IDs in the desired display order.
    /// </summary>
    public IReadOnlyList<Guid> LeaveTypeIds { get; init; } = [];
}
