namespace HRM.Application.Common.Helpers;

/// <summary>
/// Shared bank-account masking (last-4). PII rule: only the last 4 digits are ever shown outside the audited
/// <c>Payroll.ViewSensitive</c> reveal path — everything before them is replaced with <c>*</c>. Used by the
/// payroll reports (US-RPT-003/US-PAY-009 BR-2) AND the payslip PDF employee section (US-PAY-004 FR-2 /
/// ISSUE-161) so both surfaces mask identically instead of re-declaring the algorithm.
/// </summary>
public static class AccountMasking
{
    /// <summary>
    /// Returns <paramref name="account"/> with every character except the last 4 replaced by <c>*</c>
    /// (e.g. <c>"1234567890"</c> → <c>"******7890"</c>). A null/blank account returns <see cref="string.Empty"/>;
    /// an account of 4 or fewer characters is fully masked (never leaks a short number in the clear).
    /// </summary>
    public static string MaskLast4(string? account)
    {
        if (string.IsNullOrWhiteSpace(account))
            return string.Empty;
        var trimmed = account.Trim();
        if (trimmed.Length <= 4)
            return new string('*', trimmed.Length);
        return new string('*', trimmed.Length - 4) + trimmed[^4..];
    }
}
