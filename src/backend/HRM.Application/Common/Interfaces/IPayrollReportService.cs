using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Payroll reports and analytics (US-PAY-009). Pure read/aggregation over the existing
/// payroll_slip / payroll_slip_detail / payroll_adjustment data from FINALIZED runs only (BR-1).
/// No new entity / migration — reports compute on-the-fly (the pre-aggregated materialized table is a
/// documented perf follow-up).
///
/// <para>Tenant isolation (AC-5, FR-8): every query runs under the EF global query filter
/// (TenantId == ITenantContext.TenantId), so Tenant A can never see Tenant B's data.</para>
/// </summary>
public interface IPayrollReportService
{
    /// <summary>Lists the available report types for the FE sidebar (US-PAY-009 §8).</summary>
    IReadOnlyList<PayrollReportDescriptorDto> ListReportTypes();

    /// <summary>
    /// Generates a tabular report (JSON preview) with the supplied filters (FR-1, FR-3). Bank-advice
    /// account numbers are MASKED here (BR-2); use <see cref="GetBankAdvicePreviewAsync"/> for the
    /// dedicated masked bank-advice payload, and the export path for the full-number file.
    /// </summary>
    Task<Result<PayrollReportResult>> GenerateReportAsync(
        PayrollReportType reportType,
        PayrollReportQueryParams queryParams,
        CancellationToken cancellationToken = default);

    /// <summary>Returns chart-library-agnostic dashboard analytics data (FR-5).</summary>
    Task<Result<PayrollAnalyticsResult>> GetAnalyticsAsync(
        PayrollAnalyticsChartType chartType,
        PayrollReportQueryParams queryParams,
        CancellationToken cancellationToken = default);

    /// <summary>The dedicated bank-advice preview with account numbers MASKED (last 4 only, BR-2 / AC-2).</summary>
    Task<Result<BankAdvicePreviewDto>> GetBankAdvicePreviewAsync(
        PayrollReportQueryParams queryParams,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// US-RPT-003 AC-4/FR-6/NFR-3: the bank-advice with FULL (unmasked) account numbers. The caller MUST
    /// already hold <c>Payroll.ViewSensitive</c> (enforced at the controller). This method WRITES an audit
    /// record (action "PayrollReport.ViewSensitive") and persists it before returning the unmasked data.
    /// </summary>
    Task<Result<BankAdvicePreviewDto>> RevealBankAdviceAsync(
        PayrollReportQueryParams queryParams,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports a report to CSV / Excel / PDF (FR-2, AC-4). For BankAdvice the exported file carries the
    /// FULL account number (BR-2). Returns the file bytes (synchronous; large-async is deferred).
    /// </summary>
    /// <summary>
    /// US-PAY-009 AC-3: ONE employee's year-end tax statement as a PDF — month-wise, not an annual total.
    /// HR-facing; <paramref name="employeeId"/> is resolved within the caller's tenant.
    /// </summary>
    Task<Result<PayrollReportExportResult>> GetYearEndTaxStatementPdfAsync(
        Guid employeeId, int year, CancellationToken cancellationToken = default);

    /// <summary>
    /// US-PAY-009 AC-3: every employee's statement for the year, bundled as a ZIP for bulk download.
    /// </summary>
    Task<Result<PayrollReportExportResult>> GetYearEndTaxStatementsBundleAsync(
        int year, CancellationToken cancellationToken = default);

    /// <summary>
    /// US-PAY-009 AC-3 (self-service): the CURRENT user's own statement. Resolves the employee from the signed-in
    /// user rather than accepting an id, so this endpoint can never be used to read someone else's tax document.
    /// </summary>
    Task<Result<PayrollReportExportResult>> GetMyYearEndTaxStatementPdfAsync(
        int year, CancellationToken cancellationToken = default);

    Task<Result<PayrollReportExportResult>> ExportReportAsync(
        PayrollReportType reportType,
        PayrollExportFormat format,
        PayrollReportQueryParams queryParams,
        CancellationToken cancellationToken = default);
}
