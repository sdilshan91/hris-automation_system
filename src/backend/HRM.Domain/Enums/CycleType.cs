namespace HRM.Domain.Enums;

/// <summary>
/// The kind of appraisal cycle (US-PRF-004 §7 / FR-4). Drives BR-4: an employee cannot be a participant in
/// two ACTIVE cycles of the SAME type simultaneously (an annual + a quarterly may overlap; two annuals may not).
/// Stored as a string column.
/// </summary>
public enum CycleType
{
    /// <summary>A yearly performance review.</summary>
    Annual = 0,

    /// <summary>A quarterly KPI check-in.</summary>
    Quarterly = 1,

    /// <summary>A probation-period review.</summary>
    Probation = 2,
}
