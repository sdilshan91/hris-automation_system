using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Inputs to create a payroll adjustment (US-PAY-007 FR-1). A flat-amount mechanism (percentage formulas are
/// out of scope, §10). When <c>IsRecurring</c> is set, <c>RecurrenceEndMonth/Year</c> bound the series and the
/// service auto-creates a Pending record for each future period (FR-5/BR-6). For a Correction,
/// <c>ReferencePayrollSlipId</c> must point at the original slip being corrected (BR-5/FR-7).
/// </summary>
public sealed record CreateAdjustmentInput(
    Guid EmployeeId,
    string AdjustmentType,
    decimal Amount,
    string Description,
    int ApplicablePayMonth,
    int ApplicablePayYear,
    bool IsTaxable,
    bool IsRecurring,
    int? RecurrenceEndMonth,
    int? RecurrenceEndYear,
    Guid? ReferencePayrollSlipId);

/// <summary>Filters for the §8 adjustment list (US-PAY-007). All optional; null = no filter on that facet.</summary>
public sealed record AdjustmentListFilter(
    string? Status,
    string? AdjustmentType,
    int? PayMonth,
    int? PayYear,
    Guid? EmployeeId,
    int Page,
    int PageSize);

/// <summary>
/// CRUD + bulk + document operations for payroll adjustments (US-PAY-007). Tenant-scoped via ITenantContext +
/// the EF global query filter (AC-5). The run-engine integration (FR-3/FR-4) lives in
/// <see cref="IPayrollAdjustmentResolver"/>, not here.
/// </summary>
public interface IPayrollAdjustmentService
{
    /// <summary>
    /// FR-1: creates an adjustment for an employee+period. Validates the employee has an active salary
    /// assignment (BR-1); retargets the period to the next available one when the requested period has a
    /// Finalized run (BR-7) or has already entered Processing (BR-8). Recurring → auto-creates the future
    /// Pending series (FR-5/BR-6). Surfaces a non-blocking BR-3 negative-net warning for deductions.
    /// </summary>
    Task<Result<CreatePayrollAdjustmentResult>> CreateAsync(CreateAdjustmentInput input, CancellationToken cancellationToken = default);

    /// <summary>FR-6: cancels a Pending adjustment (Applied/Cancelled → 409). Tenant-scoped; cross-tenant → 404.</summary>
    Task<Result> CancelAsync(Guid adjustmentId, CancellationToken cancellationToken = default);

    /// <summary>Lists adjustments with §8 filters (status/type/period/employee), most-recent-first, paginated.</summary>
    Task<Result<PayrollAdjustmentPageDto>> ListAsync(AdjustmentListFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Gets a single adjustment by id (tenant-scoped; cross-tenant → 404).</summary>
    Task<Result<PayrollAdjustmentDto>> GetAsync(Guid adjustmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-2: bulk-creates adjustments from a parsed CSV (employee_no, adjustment_type, amount, description,
    /// is_taxable) for one period. Per-row validation; valid rows succeed, bad rows are reported with an error
    /// code, all in one SaveChanges (NFR-2). Recurring is not supported in bulk (one period per upload).
    /// </summary>
    Task<Result<BulkAdjustmentResultDto>> BulkCreateAsync(
        int payMonth, int payYear, Stream csvContent, CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-3/NFR-5: uploads a supporting document for an adjustment to tenant blob storage at
    /// {tenantId}/payroll/adjustments/{adjustmentId}/. Validates type (PDF/JPG/PNG) + size (&lt;= 5MB).
    /// Returns the stored within-tenant path.
    /// </summary>
    Task<Result<string>> UploadDocumentAsync(
        Guid adjustmentId, Stream content, string fileName, string contentType, long length,
        CancellationToken cancellationToken = default);

    /// <summary>AC-3: streams an adjustment's supporting document (tenant-scoped; missing/cross-tenant → 404).</summary>
    Task<Result<AdjustmentDocumentDto>> DownloadDocumentAsync(Guid adjustmentId, CancellationToken cancellationToken = default);
}
