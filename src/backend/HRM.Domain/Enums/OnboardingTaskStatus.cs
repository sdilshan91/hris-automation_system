namespace HRM.Domain.Enums;

/// <summary>
/// Status of an individual assigned onboarding task instance (US-ONB-002 AC-2). New task instances are
/// created <see cref="Pending"/>. Stored as a string column for readability/forward-compat.
/// </summary>
public enum OnboardingTaskStatus
{
    /// <summary>Created and awaiting action (default on assignment, AC-2).</summary>
    Pending = 0,

    /// <summary>Work has started.</summary>
    InProgress = 1,

    /// <summary>Completed.</summary>
    Completed = 2,

    /// <summary>Skipped (non-mandatory only, BR-3).</summary>
    Skipped = 3,
}
