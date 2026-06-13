namespace HRM.Domain.Enums;

/// <summary>
/// Types of leave ledger entries (US-LV-002 FR-5).
/// Each entry records a transaction against an employee's leave balance.
/// </summary>
public enum LedgerEntryType
{
    Accrual = 0,
    Used = 1,
    Adjusted = 2,
    Encashed = 3,
    CarryForward = 4,
    Expired = 5,
}
