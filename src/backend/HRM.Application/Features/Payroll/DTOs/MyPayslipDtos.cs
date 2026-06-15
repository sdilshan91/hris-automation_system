namespace HRM.Application.Features.Payroll.DTOs;

/// <summary>
/// One row of the employee self-service payslip list (US-PAY-005 AC-1 / §7 List shape). Only payslips from
/// Finalized runs (BR-1) reach this DTO. Matches the FE camelCase contract exactly.
/// </summary>
public sealed record MyPayslipListItemDto
{
    public required Guid PayslipId { get; init; }
    public required int PayMonth { get; init; }
    public required int PayYear { get; init; }
    public required decimal GrossEarnings { get; init; }
    public required decimal TotalDeductions { get; init; }
    public required decimal NetSalary { get; init; }
    public required decimal PaidDays { get; init; }
    public required decimal LopDays { get; init; }
    /// <summary>True when the pre-generated PDF (US-PAY-004) is available for download.</summary>
    public required bool PdfAvailable { get; init; }
}

/// <summary>
/// A page of the employee's own payslips (US-PAY-005 AC-1/FR-6, §7). Most-recent-first; default 12/page
/// (one year of monthly payslips). Matches the FE list-response contract.
/// </summary>
public sealed record MyPayslipListDto
{
    public required IReadOnlyList<MyPayslipListItemDto> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
}

/// <summary>Employee identity block shown on the payslip detail (US-PAY-005 §7 Detail.employee).</summary>
public sealed record MyPayslipEmployeeDto
{
    public required string Name { get; init; }
    public required string EmployeeNo { get; init; }
    public string? Department { get; init; }
    public string? Designation { get; init; }
}

/// <summary>
/// One component line on the payslip detail (US-PAY-005 §7 Detail.earnings/deductions). <see cref="YtdAmount"/>
/// is populated only when the tenant has YTD display enabled (FR-7; currently defaults off — see module note).
/// </summary>
public sealed record MyPayslipComponentDto
{
    public required string ComponentName { get; init; }
    public required decimal Amount { get; init; }
    public decimal? YtdAmount { get; init; }
}

/// <summary>
/// Full self-service payslip detail (US-PAY-005 AC-2/FR-3, §7 Detail shape): identity, earnings + deductions
/// breakdown from payroll_slip_detail, rolled-up totals, and the attendance basis. Only accessible for the
/// authenticated employee's own slip from a Finalized run (BR-1/BR-5; cross-employee access → 403).
/// </summary>
public sealed record MyPayslipDetailDto
{
    public required Guid PayslipId { get; init; }
    public required int PayMonth { get; init; }
    public required int PayYear { get; init; }
    public required MyPayslipEmployeeDto Employee { get; init; }
    public required IReadOnlyList<MyPayslipComponentDto> Earnings { get; init; }
    public required IReadOnlyList<MyPayslipComponentDto> Deductions { get; init; }
    public required decimal GrossEarnings { get; init; }
    public required decimal TotalDeductions { get; init; }
    public required decimal NetSalary { get; init; }
    public required decimal WorkingDays { get; init; }
    public required decimal PaidDays { get; init; }
    public required decimal LopDays { get; init; }
}
