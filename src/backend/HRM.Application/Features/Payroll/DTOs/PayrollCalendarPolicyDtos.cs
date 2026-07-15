namespace HRM.Application.Features.Payroll.DTOs;

/// <summary>A single effective-dated payroll calendar policy version (US-ATT-011 AC-4 / CAL-5).</summary>
public sealed record PayrollCalendarPolicyDto
{
    /// <summary>The policy row id. <see cref="System.Guid.Empty"/> when this is the (unpersisted) code-default.</summary>
    public Guid Id { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    /// <summary>Whether public holidays are excluded from the payroll working-days count. Code-default: false.</summary>
    public bool ExcludeHolidaysFromWorkingDays { get; init; }
    public bool IsActive { get; init; }
    /// <summary>True when this DTO is the code-default (no persisted policy configured for the tenant).</summary>
    public bool IsDefault { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Request body to configure a new effective-dated payroll calendar policy version (US-ATT-011 AC-4 / CAL-5).
/// <see cref="ExcludeHolidaysFromWorkingDays"/> defaults to FALSE: an omitted flag must never silently change a
/// tenant's OT base / LOP rate.
/// </summary>
public sealed record CreatePayrollCalendarPolicyRequest
{
    public DateOnly EffectiveFrom { get; init; }
    public bool ExcludeHolidaysFromWorkingDays { get; init; } = false;
    public bool IsActive { get; init; } = true;
}
