namespace HRM.Domain.Enums;

/// <summary>
/// Classification of one line on a Full-and-Final settlement (ISSUE-294 Phase 1). Stored as a string. Earnings
/// (pro-rated final pay) and encashment ADD to the net payable; statutory SUBTRACTS.
/// </summary>
public enum FinalSettlementLineType
{
    /// <summary>A pro-rated final-month earning/reimbursement component (adds to the net).</summary>
    Earning = 0,

    /// <summary>An employee-side statutory deduction (income tax / EPF / etc.) — subtracts from the net.</summary>
    Statutory = 1,

    /// <summary>Forfeitable leave encashment (adds to the net).</summary>
    Encashment = 2,
}
