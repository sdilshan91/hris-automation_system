namespace HRM.Domain.Enums;

/// <summary>
/// Classifies a payroll adjustment (US-PAY-007 FR-1). Serialized as a string in the API
/// (global JsonStringEnumConverter) and stored as varchar(20) via HasConversion&lt;string&gt;().
///
/// <para>Effect on the payslip when the run engine picks the adjustment up (FR-3):</para>
/// <list type="bullet">
///   <item><see cref="Bonus"/> — an earning that adds to gross; subject to tax only when <c>IsTaxable</c> (BR-2).</item>
///   <item><see cref="Deduction"/> — subtracted from net (BR-3); a deduction that would make net negative warns (BR-3).</item>
///   <item><see cref="Reimbursement"/> — a non-taxable earning by default (BR-4); adds to gross, outside tax.</item>
///   <item><see cref="Correction"/> — an arrears line referencing the original slip (BR-5/FR-7); shown as "Arrears".</item>
/// </list>
/// </summary>
public enum AdjustmentType
{
    /// <summary>Ad-hoc bonus — an earning added to gross; taxable when <c>IsTaxable</c> (BR-2).</summary>
    Bonus,

    /// <summary>One-time deduction — subtracted from net pay (BR-3).</summary>
    Deduction,

    /// <summary>Expense reimbursement — non-taxable earning by default (BR-4).</summary>
    Reimbursement,

    /// <summary>Correction / arrears — references the original payslip; shown as "Arrears" (BR-5/FR-7).</summary>
    Correction,
}
