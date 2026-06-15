namespace HRM.Domain.Enums;

/// <summary>
/// Pay frequency for an offered salary (US-REC-007 FR-1). Stored as a string column for
/// readability/forward-compat (the codebase's varchar-enum pattern). API uses a global
/// JsonStringEnumConverter, so the FE consumes these exact PascalCase member names (US-PLT-003).
/// </summary>
public enum SalaryFrequency
{
    /// <summary>Salary expressed per month. Default.</summary>
    Monthly = 0,

    /// <summary>Salary expressed per year.</summary>
    Annual = 1,

    /// <summary>Salary expressed per week.</summary>
    Weekly = 2,

    /// <summary>Salary expressed per hour.</summary>
    Hourly = 3,
}
