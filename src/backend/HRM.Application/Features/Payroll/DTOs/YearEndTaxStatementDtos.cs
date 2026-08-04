namespace HRM.Application.Features.Payroll.DTOs;

/// <summary>
/// One month's tax line inside an employee's fiscal-year statement (US-PAY-009 AC-3).
///
/// <para>AC-3 asks for a <b>month-wise</b> breakdown. The columnar report aggregates each employee to a single
/// annual row, which cannot satisfy that — an employee filing with a tax authority needs to see the periods,
/// not one total.</para>
/// </summary>
public sealed record YearEndTaxMonthLineDto
{
    public int PayYear { get; init; }
    public int PayMonth { get; init; }

    /// <summary>Month name for display, e.g. "April 2026".</summary>
    public string PeriodLabel { get; init; } = string.Empty;

    public decimal GrossEarnings { get; init; }
    public decimal TaxableIncome { get; init; }
    public decimal IncomeTaxWithheld { get; init; }
    public decimal TotalDeductions { get; init; }
}

/// <summary>
/// One employee's year-end tax statement (US-PAY-009 AC-3): who they are, which fiscal year, the month-wise
/// lines, and the totals a tax authority expects.
/// </summary>
public sealed record YearEndTaxStatementDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeNo { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public string? Designation { get; init; }

    /// <summary>The employee's resolved tax country (branch country → tenant default → sole rule country).</summary>
    public string CountryCode { get; init; } = string.Empty;

    /// <summary>The fiscal-year label from that country's in-effect income-tax rule, e.g. "2026-2027".</summary>
    public string FiscalYear { get; init; } = string.Empty;

    /// <summary>Month-wise lines inside the employee's fiscal-year window, chronological.</summary>
    public IReadOnlyList<YearEndTaxMonthLineDto> Lines { get; init; } = [];

    public decimal TotalGrossEarnings { get; init; }
    public decimal TotalTaxableIncome { get; init; }
    public decimal TotalIncomeTaxWithheld { get; init; }
    public decimal TotalDeductions { get; init; }

    /// <summary>The tenant's display name, stamped for the document header.</summary>
    public string TenantName { get; init; } = string.Empty;
}
