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
    SalaryRevision = 4
}
