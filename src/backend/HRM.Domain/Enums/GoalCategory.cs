namespace HRM.Domain.Enums;

/// <summary>
/// Category of a performance goal (US-PRF-001 FR-2). Stored as a string column for
/// readability/forward-compat (see GoalConfiguration).
/// </summary>
public enum GoalCategory
{
    /// <summary>A measurable Key Performance Indicator.</summary>
    Kpi = 0,

    /// <summary>A behavioural / skill competency objective.</summary>
    Competency = 1,

    /// <summary>A project-delivery objective.</summary>
    Project = 2,
}
