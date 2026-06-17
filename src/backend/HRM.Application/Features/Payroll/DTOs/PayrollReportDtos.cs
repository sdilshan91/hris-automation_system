namespace HRM.Application.Features.Payroll.DTOs;

/// <summary>
/// The pre-built payroll report types (US-PAY-009 FR-1). Sent as the {reportType} path segment
/// (case-insensitive). All read over the existing payroll_slip / payroll_slip_detail / payroll_adjustment
/// data from FINALIZED runs only (BR-1).
///
/// <para>Implemented (this story): PayrollSummary (FR-1a), EmployeeRegister (FR-1b),
/// DepartmentSummary (FR-1c), StatutoryDeduction (FR-1d), BankAdvice (FR-1e/AC-2), Ctc (FR-1h),
/// Variance (FR-1g). YearEndTaxStatement (FR-1f) is a documented stub (returns a deferral note) —
/// the bulk 5,000-PDF + ZIP path is deferred per the story brief.</para>
/// </summary>
public enum PayrollReportType
{
    /// <summary>
    /// FR-1a: period totals + department-wise breakdown. US-RPT-003 AC-1/FR-3: this report's result
    /// ADDITIONALLY carries an embedded <see cref="PayrollReportResult.Summary"/> block (period TOTALS —
    /// gross, deductions, net, employee count — with a month-over-month comparison vs the previous finalized
    /// period) that drives the FE KPI cards + dual-bar chart. The department-breakdown table is unchanged.
    /// </summary>
    PayrollSummary,
    /// <summary>FR-1b: all employees with component-wise breakdown for a period.</summary>
    EmployeeRegister,
    /// <summary>FR-1c: department-wise payroll summary (the per-department slice of FR-1a).</summary>
    DepartmentSummary,
    /// <summary>FR-1d: tax / EPF / ETF statutory summaries for filing.</summary>
    StatutoryDeduction,
    /// <summary>FR-1e / AC-2: bank advice file for salary disbursement (account masked in preview, BR-2).</summary>
    BankAdvice,
    /// <summary>FR-1h: current CTC of all employees (from employee_salary_component).</summary>
    Ctc,
    /// <summary>FR-1g: month-over-month variance, &gt;10% changes flagged (BR-4).</summary>
    Variance,
    /// <summary>FR-1f: year-end tax statements — DEFERRED stub (returns a documented note, no rows).</summary>
    YearEndTaxStatement,
}

/// <summary>
/// The payroll analytics chart types for the dashboard API (US-PAY-009 FR-5). Returned as
/// chart-library-agnostic series so the FE can feed any charting lib.
/// </summary>
public enum PayrollAnalyticsChartType
{
    /// <summary>Monthly payroll trend — gross / deductions / net over the last 12 finalized months (line).</summary>
    MonthlyTrend,
    /// <summary>Department cost distribution — total net per department for a period (pie/bar).</summary>
    DepartmentCostDistribution,
    /// <summary>Statutory deduction breakdown — total per statutory component for a period (stacked bar).</summary>
    StatutoryBreakdown,
}

/// <summary>
/// Export file format (US-PAY-009 FR-2, AC-4). CSV + Excel (ClosedXML) + PDF (QuestPDF).
/// Distinct from the leave module's two-value enum because payroll reports add PDF (FR-2).
/// </summary>
public enum PayrollExportFormat
{
    Csv,
    Xlsx,
    Pdf,
}

/// <summary>
/// Common filter parameters shared by all payroll reports (US-PAY-009 FR-3). Filters that don't apply
/// to a given report are ignored by that report's query. A report's "period" is (PayMonth, PayYear).
/// </summary>
public sealed record PayrollReportQueryParams
{
    /// <summary>Pay period month, 1-12. Defaults to the most recent finalized run's month when omitted.</summary>
    public int? PayMonth { get; init; }
    /// <summary>Pay period year. Defaults to the most recent finalized run's year when omitted.</summary>
    public int? PayYear { get; init; }
    /// <summary>Filter to a single department.</summary>
    public Guid? DepartmentId { get; init; }
    /// <summary>Filter to a single job title (designation proxy — no JobLevel entity exists).</summary>
    public Guid? JobTitleId { get; init; }
    /// <summary>Filter to a single employment type (FullTime / PartTime / Contract / Intern).</summary>
    public string? EmploymentType { get; init; }
    /// <summary>Free-text employee search (matches name or employee number, case-insensitive).</summary>
    public string? EmployeeSearch { get; init; }

    // ── US-RPT-003 FR-2/FR-4: additional filters ────────────────────────────────

    /// <summary>
    /// US-RPT-003 FR-4: a SPECIFIC finalized payroll run (e.g. a supplementary run for the period). When set
    /// the report reads only this run's slips; when omitted, ALL finalized runs for the resolved period are
    /// included (defaults to the latest finalized period via the period resolver).
    /// </summary>
    public Guid? PayrollRunId { get; init; }

    /// <summary>US-RPT-003 FR-2: filter to a single work location (Employee.LocationId).</summary>
    public Guid? LocationId { get; init; }

    /// <summary>
    /// US-RPT-003 FR-2: filter by pay grade. No PayGrade entity is modelled on the employee yet, so this
    /// matches the free-text <see cref="HRM.Domain.Entities.Employee.Location"/>-style fields where present;
    /// when no pay-grade field exists it is accepted but has no effect (documented). Optional.
    /// </summary>
    public string? PayGrade { get; init; }
}

/// <summary>
/// Server-side report envelope (US-PAY-009). Mirrors the established LeaveReportResult shape
/// (Columns + Rows) so the same shape feeds the JSON table, the CSV writer, the XLSX writer and the PDF
/// table. Reports are read synchronously (async-large via Hangfire is deferred per the brief).
/// </summary>
public sealed record PayrollReportResult
{
    /// <summary>The report type that was generated (echoed for the FE).</summary>
    public string ReportType { get; init; } = string.Empty;
    /// <summary>Human-readable title, e.g. "Payroll Summary — May 2026".</summary>
    public string Title { get; init; } = string.Empty;
    /// <summary>The resolved pay period the report covers (echoed so the FE can label filters).</summary>
    public int PayMonth { get; init; }
    public int PayYear { get; init; }
    /// <summary>Ordered column headers (drive the table + the export header row).</summary>
    public IReadOnlyList<string> Columns { get; init; } = [];
    /// <summary>The report rows. Each row is an ordered list of cell values (string-formatted).</summary>
    public IReadOnlyList<PayrollReportRow> Rows { get; init; } = [];
    /// <summary>Optional grand-total / footer row aligned to <see cref="Columns"/> (null when not applicable).</summary>
    public PayrollReportRow? TotalRow { get; init; }
    /// <summary>Total row count.</summary>
    public int TotalCount { get; init; }
    /// <summary>Optional note (e.g. a documented stub / deferral, or the bank-fields gap). Null otherwise.</summary>
    public string? Note { get; init; }
    /// <summary>
    /// US-RPT-003 AC-1/FR-3: OPTIONAL KPI + month-over-month summary block. Populated ONLY for the
    /// <see cref="PayrollReportType.PayrollSummary"/> report; null for every other report type (the FE
    /// treats it as optional). Drives the KPI cards + the MoM dual-bar chart on top of the existing
    /// department-breakdown table.
    /// </summary>
    public PayrollReportSummaryDto? Summary { get; init; }
}

/// <summary>
/// One row in a payroll report — ordered cell values aligned to <see cref="PayrollReportResult.Columns"/>.
/// Strings so the same shape feeds the JSON table, the CSV writer, the XLSX writer and the PDF table.
/// </summary>
public sealed record PayrollReportRow
{
    public IReadOnlyList<string> Cells { get; init; } = [];
}

/// <summary>A single labelled data point for a chart series (US-PAY-009 FR-5). Chart-library-agnostic.</summary>
public sealed record PayrollChartPoint
{
    public string Label { get; init; } = string.Empty;
    public decimal Value { get; init; }
}

/// <summary>A named series of <see cref="PayrollChartPoint"/>s (e.g. "Gross" across 12 months).</summary>
public sealed record PayrollChartSeries
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<PayrollChartPoint> Points { get; init; } = [];
}

/// <summary>
/// The analytics payload for a payroll dashboard chart (US-PAY-009 FR-5). A single-series chart uses
/// <see cref="Points"/>; a multi-series chart (monthly trend = gross/deductions/net) uses
/// <see cref="Series"/> with shared <see cref="Categories"/> (the X axis, month labels). Either may be empty.
/// </summary>
public sealed record PayrollAnalyticsResult
{
    public string ChartType { get; init; } = string.Empty;
    /// <summary>Single-series points (DepartmentCostDistribution / StatutoryBreakdown).</summary>
    public IReadOnlyList<PayrollChartPoint> Points { get; init; } = [];
    /// <summary>Shared X-axis category labels for multi-series charts (e.g. "2025-07" … "2026-06").</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];
    /// <summary>Multi-series data (MonthlyTrend — gross/deductions/net series).</summary>
    public IReadOnlyList<PayrollChartSeries> Series { get; init; } = [];
}

/// <summary>
/// US-RPT-003 AC-1/FR-3: the EMBEDDED Payroll Run Summary block carried on the
/// <see cref="PayrollReportResult.Summary"/> of the PayrollSummary report — period TOTALS (gross,
/// deductions, net, employee count) with a month-over-month comparison vs the previous finalized period.
/// Drives the FE KPI cards (with up/down arrows) and the dual-bar comparison chart. The shape matches the
/// FE <c>IPayrollRunSummary</c> contract exactly (System.Text.Json camelCase serialization).
/// </summary>
public sealed record PayrollReportSummaryDto
{
    /// <summary>BR-2: the tenant's configured ISO-4217 currency code (e.g. "USD").</summary>
    public string Currency { get; init; } = string.Empty;
    /// <summary>The current period label, e.g. "May 2026".</summary>
    public string CurrentLabel { get; init; } = string.Empty;
    /// <summary>The previous period label, e.g. "April 2026"; null when there is no prior finalized run.</summary>
    public string? PreviousLabel { get; init; }
    /// <summary>The four headline metrics (gross / deductions / net / employeeCount) with MoM variance.</summary>
    public IReadOnlyList<PayrollSummaryMetricDto> Metrics { get; init; } = [];
}

/// <summary>
/// US-RPT-003 AC-1/FR-3: one headline summary metric with its current + previous value and the signed
/// variance (current − previous). Matches the FE <c>IPayrollSummaryMetric</c> contract. <see cref="Previous"/>
/// and <see cref="Variance"/> are null when there is no prior finalized period to compare against.
/// </summary>
public sealed record PayrollSummaryMetricDto
{
    /// <summary>Stable camelCase key: gross / deductions / net / employeeCount.</summary>
    public string Key { get; init; } = string.Empty;
    /// <summary>Human-readable label, e.g. "Total Gross".</summary>
    public string Label { get; init; } = string.Empty;
    /// <summary>Current-period value (money amount, or the employee count for the count metric).</summary>
    public decimal Current { get; init; }
    /// <summary>Previous-period value; null when there is no previous period.</summary>
    public decimal? Previous { get; init; }
    /// <summary>Signed variance (current − previous); null when there is no previous period.</summary>
    public decimal? Variance { get; init; }
    /// <summary>True for the three money cost metrics (gross/deductions/net); false for employeeCount.</summary>
    public bool IsCost { get; init; }
}

/// <summary>
/// One bank-advice line (US-PAY-009 FR-1e / AC-2 / §7). The JSON preview returns the account number
/// MASKED (last 4 only, BR-2); the exported file (CSV/Excel/PDF) carries the FULL account number.
/// </summary>
public sealed record BankAdviceLineDto
{
    public string EmployeeNo { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public string BankName { get; init; } = string.Empty;
    public string BranchCode { get; init; } = string.Empty;
    /// <summary>Masked (last 4 only) in the JSON preview; FULL in the exported file (BR-2).</summary>
    public string AccountNumber { get; init; } = string.Empty;
    public decimal NetAmount { get; init; }
    public string Narration { get; init; } = string.Empty;
}

/// <summary>
/// The bank-advice preview payload (US-PAY-009 AC-2). Account numbers are MASKED here (BR-2); the
/// downloadable file (via the export endpoint with reportType=BankAdvice) carries full numbers.
/// </summary>
public sealed record BankAdvicePreviewDto
{
    public int PayMonth { get; init; }
    public int PayYear { get; init; }
    public IReadOnlyList<BankAdviceLineDto> Lines { get; init; } = [];
    public int EmployeeCount { get; init; }
    public decimal TotalNetAmount { get; init; }
    /// <summary>
    /// US-RPT-003 FR-6: true when account numbers in <see cref="Lines"/> are MASKED (last-4 only, the
    /// default). False only on the audited Payroll.ViewSensitive reveal path, where the full number is served.
    /// </summary>
    public bool Masked { get; init; } = true;
    /// <summary>Optional note (e.g. the audited-reveal note). Null otherwise.</summary>
    public string? Note { get; init; }
}

/// <summary>
/// One report-type descriptor for the report-list endpoint (US-PAY-009). Lets the FE render the
/// Notion-style sidebar of available reports with stable identifiers + which filters apply.
/// </summary>
public sealed record PayrollReportDescriptorDto
{
    /// <summary>Stable identifier = the <see cref="PayrollReportType"/> name (the {reportType} path value).</summary>
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    /// <summary>True when this report is a documented stub / deferred (FE can disable or badge it).</summary>
    public bool Deferred { get; init; }
}

/// <summary>
/// Result of a report export (US-PAY-009 FR-2, AC-4). Reports are exported synchronously (the async-large
/// Hangfire path is deferred per the brief), so the file bytes are always populated on success.
/// </summary>
public sealed record PayrollReportExportResult
{
    public byte[] FileContent { get; init; } = [];
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
}
