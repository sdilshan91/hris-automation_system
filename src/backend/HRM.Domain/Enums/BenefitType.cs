namespace HRM.Domain.Enums;

/// <summary>
/// Category of a benefit plan (US-TRN-002 FR-1). Stored as a string column.
/// </summary>
public enum BenefitType
{
    Health = 0,
    Dental = 1,
    Vision = 2,
    Life = 3,
    Retirement = 4,
    Disability = 5,
    Other = 6,
}
