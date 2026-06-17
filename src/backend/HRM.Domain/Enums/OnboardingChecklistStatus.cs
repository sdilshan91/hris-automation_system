namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of an assigned onboarding checklist instance (US-ONB-002). A new hire can have at
/// most one <see cref="Active"/> checklist at a time (BR-2); replacing one supersedes the prior version.
/// Stored as a string column for readability/forward-compat.
/// </summary>
public enum OnboardingChecklistStatus
{
    /// <summary>The currently active checklist for the employee (BR-2: at most one).</summary>
    Active = 0,

    /// <summary>All tasks completed.</summary>
    Completed = 1,

    /// <summary>Superseded by a newer version (BR-2 replace) — kept for history, not editable.</summary>
    Superseded = 2,

    /// <summary>Cancelled before completion.</summary>
    Cancelled = 3,
}
