namespace HRM.Domain.Enums;

/// <summary>
/// Discriminates an <see cref="Entities.OnboardingChecklistTemplate"/> between onboarding and offboarding
/// (US-ONB-005 FR-1). Offboarding templates reuse the same template/task entities so the builder, EF config,
/// and tenant isolation are shared (DRY). Stored as a string column; defaults to
/// <see cref="Onboarding"/> so every pre-existing template keeps its original meaning.
/// </summary>
public enum ChecklistKind
{
    /// <summary>A new-hire onboarding checklist template (US-ONB-001, the original/default).</summary>
    Onboarding = 0,

    /// <summary>A departing-employee offboarding / exit-clearance template (US-ONB-005 FR-1).</summary>
    Offboarding = 1,
}
