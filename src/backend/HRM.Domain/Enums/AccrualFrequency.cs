namespace HRM.Domain.Enums;

/// <summary>
/// Frequency at which leave entitlement is accrued (US-LV-001 FR-2).
/// </summary>
public enum AccrualFrequency
{
    Monthly = 0,
    Quarterly = 1,
    Yearly = 2,
    Upfront = 3,
}
