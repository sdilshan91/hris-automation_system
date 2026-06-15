using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Payslip-PDF generation service (US-PAY-004). Tenant-scoped via ITenantContext + the EF global query
/// filter (AC-4). Enqueues a Hangfire batch job to render + store each slip's PDF (AC-1/FR-4), reports
/// generation status (FR-7), and streams single / bulk-ZIP downloads (FR-6). The heavy batch render lives
/// in <see cref="IPayslipBatchRenderer"/> (invoked by the job, or directly in tests).
/// </summary>
public interface IPayslipGenerationService
{
    /// <summary>
    /// Marks the run's slips Pending and enqueues the GeneratePayslipsJob (AC-1/FR-4). Only ReviewPending /
    /// Approved / Finalized runs (BR-1) — else 400. Regenerate overwrites the prior PDFs (AC-5). When no
    /// Hangfire scheduler is registered (tests/dev), processing must be triggered directly via the batch renderer.
    /// </summary>
    Task<Result<PayslipGenerationAcceptedDto>> GenerateAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Per-status counts for the run's slips (FR-7, §8 status bar).</summary>
    Task<Result<PayslipGenerationStatusDto>> GetStatusAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the run's payslips for the §8 table (employee name/no/department, net salary, PDF status).
    /// Tenant-scoped via the global query filter (AC-4). 404 when the run does not exist for the tenant.
    /// </summary>
    Task<Result<IReadOnlyList<PayslipListItemDto>>> ListForRunAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams ONE employee's payslip PDF (FR-6). 404 when the slip or its stored PDF does not exist; the
    /// global query filter rejects cross-tenant access (AC-4). File name is BR-5
    /// <c>{EmployeeNo}_{PayMonth}_{PayYear}.pdf</c>.
    /// </summary>
    Task<Result<PayslipFileDto>> DownloadOneAsync(Guid runId, Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a ZIP archive of all generated payslip PDFs in the run (FR-6/AC-3). Entry names are BR-5.
    /// Skips slips with no stored PDF. 404 when the run has no generated payslips.
    /// </summary>
    Task<Result<PayslipFileDto>> DownloadAllZipAsync(Guid runId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The compute side of payslip generation (US-PAY-004 FR-4/FR-7). Invoked by the Hangfire GeneratePayslipsJob
/// after it restores the tenant context (so the global query filter scopes to the run's tenant — AC-4).
/// Separated from the job so the batch render can be exercised directly in tests without a live Hangfire
/// server. Renders each slip's PDF, stores it via <see cref="IFileStorage"/>, and updates the slip's PDF
/// fields. Bounded concurrency (NFR-3); failed renders → PdfStatus=Failed + logged, retryable (FR-8).
/// </summary>
public interface IPayslipBatchRenderer
{
    /// <summary>Renders + stores all slips for the run; returns (generated, failed) counts.</summary>
    Task<Result<PayslipBatchResult>> RenderRunAsync(Guid runId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a payslip batch render (US-PAY-004 FR-7/FR-8).</summary>
public sealed record PayslipBatchResult(int Generated, int Failed);

/// <summary>
/// Seam that enqueues the tenant-aware GeneratePayslipsJob (US-PAY-004 FR-4). Implemented in HRM.Api over
/// Hangfire's IBackgroundJobClient; OPTIONAL in DI — when absent (tests/dev without Hangfire storage) the
/// generation service skips enqueue and the batch renderer can be invoked directly. Mirrors
/// IPayrollRunJobScheduler.
/// </summary>
public interface IPayslipGenerationJobScheduler
{
    /// <summary>Enqueues PDF generation of <paramref name="runId"/> for <paramref name="tenantId"/> (FR-4).</summary>
    string Enqueue(Guid tenantId, string tenantSubdomain, Guid runId);
}
