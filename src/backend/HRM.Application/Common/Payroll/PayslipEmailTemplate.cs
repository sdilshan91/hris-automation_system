using System.Globalization;

namespace HRM.Application.Common.Payroll;

/// <summary>
/// Builds the subject + body for a payslip email (US-PAY-011 FR-3/AC-2/NFR-5). The tenant template is the
/// system default (BR-2 — per-tenant template config is deferred, see the module note). The supported
/// variables are {EmployeeName}/{PayMonth}/{PayYear}/{CompanyName} ONLY — deliberately NOT {NetSalary} or any
/// other amount (NFR-5: salary figures live only in the attached PDF, never in the email body). Pure +
/// side-effect-free so it is trivially unit-testable.
/// </summary>
public static class PayslipEmailTemplate
{
    private static readonly string[] MonthNames =
    {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    };

    /// <summary>AC-2 subject: <c>Your Payslip for {Month} {Year}</c>.</summary>
    public static string BuildSubject(int payMonth, int payYear)
        => $"Your Payslip for {MonthName(payMonth)} {payYear}";

    /// <summary>
    /// FR-3/NFR-5 body. Substitutes the allowed variables only; carries no salary amounts. Plain text so it
    /// renders without an HTML pipeline and a real sender can wrap it later.
    /// </summary>
    public static string BuildBody(string employeeName, int payMonth, int payYear, string companyName)
    {
        var name = string.IsNullOrWhiteSpace(employeeName) ? "Employee" : employeeName.Trim();
        var company = string.IsNullOrWhiteSpace(companyName) ? "your company" : companyName.Trim();
        var month = MonthName(payMonth);

        return
            $"Dear {name},\n\n" +
            $"Your payslip for {month} {payYear} from {company} is attached to this email as a PDF document.\n\n" +
            "Please keep it for your records. If you have any questions about your payslip, contact your HR department.\n\n" +
            $"Regards,\n{company} Payroll Team";
    }

    private static string MonthName(int payMonth)
        => payMonth is >= 1 and <= 12
            ? MonthNames[payMonth - 1]
            : payMonth.ToString(CultureInfo.InvariantCulture);
}
