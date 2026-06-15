namespace HRM.Domain.Enums;

/// <summary>
/// Classifies a salary component (US-PAY-001 FR-1). Serialized as a string in the API
/// (global JsonStringEnumConverter) and stored as varchar via HasConversion&lt;string&gt;().
/// </summary>
public enum SalaryComponentType
{
    /// <summary>Adds to gross pay (e.g. Basic, HRA, Allowances).</summary>
    Earning,

    /// <summary>Reduces net pay (e.g. loan repayment, advance recovery).</summary>
    Deduction,

    /// <summary>Statutory item (e.g. EPF, ETF, PAYE). Flagged via <c>IsStatutory</c> (BR-3).</summary>
    Statutory,

    /// <summary>Non-taxable expense reimbursement (e.g. travel, mobile).</summary>
    Reimbursement,
}
