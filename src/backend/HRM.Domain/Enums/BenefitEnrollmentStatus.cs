namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of a benefit enrollment (US-TRN-003 FR-2). Stored as a string column. Only one
/// <see cref="Active"/> enrollment may exist per (plan, employee) — enforced by a partial unique index.
/// Termination is a soft state change to <see cref="Terminated"/> (BR-5), never a hard delete.
/// </summary>
public enum BenefitEnrollmentStatus
{
    Active = 0,
    Pending = 1,
    Declined = 2,
    Terminated = 3,
}
