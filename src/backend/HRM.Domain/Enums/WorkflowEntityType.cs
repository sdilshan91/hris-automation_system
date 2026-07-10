namespace HRM.Domain.Enums;

/// <summary>
/// The request/entity type an approval workflow definition governs (US-ADM-007 FR-1).
/// Each entity type can have only one ACTIVE workflow at a time per tenant (BR-2).
/// </summary>
public enum WorkflowEntityType
{
    Leave = 0,
    Attendance = 1,
    Expense = 2,
    Offer = 3,
    SalaryRevision = 4,

    /// <summary>
    /// US-ADM-011c (Q6): overtime pre-approval requests. Appended (int-stored) — no migration/seeding needed,
    /// as there is no per-entity-type default-workflow seeding (the legacy fallback is the default state).
    /// </summary>
    Overtime = 5
}
