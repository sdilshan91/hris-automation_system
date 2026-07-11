using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Payroll;

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
    /// <summary>
    /// Renders + stores all slips for the run; returns (generated, failed) counts. Thin convenience that runs
    /// the three phases below in sequence for non-job callers (tests) — the Hangfire job orchestrates the
    /// phases itself so the heavy render/upload runs OUTSIDE any tenant-GUC transaction (ISSUE-269).
    /// </summary>
    Task<Result<PayslipBatchResult>> RenderRunAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ISSUE-269 phase 1 — READ. Bulk-loads (AsNoTracking) the run + slips + employees + details + branding and
    /// projects them to non-tracked value snapshots so the WORK phase carries no tracked entities. Runs inside a
    /// short tenant-GUC transaction (the job wraps it in <see cref="ITenantJobRunner"/>). Returns the render plan
    /// (empty Items when the run has no slips). 404 when the run does not exist for the tenant.
    /// </summary>
    Task<Result<PayslipRenderPlan>> LoadRenderPlanAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ISSUE-269 phase 2 — WORK (no DB, no transaction). The bounded-concurrency (NFR-3) render + storage-upload
    /// loop over the plan snapshots. Pure CPU + file IO; the job runs it OUTSIDE any runner call so no tenant-GUC
    /// transaction is held idle through the minutes-long render. Returns one outcome per slip.
    /// </summary>
    Task<IReadOnlyList<PayslipRenderOutcome>> RenderAndUploadAsync(
        PayslipRenderPlan plan, CancellationToken cancellationToken = default);

    /// <summary>
    /// ISSUE-269 phase 3 — WRITE (one short transaction per chunk). Loads just this chunk's slips fresh, applies
    /// the four PDF fields (success/failure exactly as before), SaveChanges, then clears the change tracker so it
    /// stays bounded across chunks of a large run. The job calls this per ~200-slip chunk.
    /// </summary>
    Task PersistRenderResultsAsync(
        IReadOnlyList<PayslipRenderOutcome> chunk, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a payslip batch render (US-PAY-004 FR-7/FR-8).</summary>
public sealed record PayslipBatchResult(int Generated, int Failed);

/// <summary>
/// ISSUE-269: an immutable snapshot of everything the render+upload WORK phase needs — NO tracked entities, so it
/// can safely outlive the READ transaction and be processed with no DbContext access.
/// </summary>
public sealed record PayslipRenderPlan(
    Guid RunId,
    Guid TenantId,
    PayslipBranding Branding,
    IReadOnlyDictionary<Guid, string> Departments,
    IReadOnlyDictionary<Guid, string> JobTitles,
    bool ShowYtd,
    IReadOnlyDictionary<(Guid EmployeeId, bool IsDeductionSide), Dictionary<string, decimal>>? YtdByComponent,
    IReadOnlyList<PayslipRenderItem> Items);

/// <summary>One slip's non-tracked render inputs.</summary>
public sealed record PayslipRenderItem(
    Guid SlipId,
    Guid EmployeeId,
    PayslipSlipSnapshot Slip,
    PayslipEmployeeSnapshot? Employee,
    IReadOnlyList<PayslipDetailSnapshot> Details);

/// <summary>The <see cref="PayslipBatchResult"/>-relevant scalars of a slip, copied off the tracked entity.</summary>
public sealed record PayslipSlipSnapshot(
    Guid EmployeeId,
    int PayMonth, int PayYear,
    decimal GrossEarnings, decimal TotalDeductions, decimal NetSalary,
    decimal WorkingDays, decimal PaidDays, decimal LopDays);

/// <summary>The employee scalars the payslip model needs, copied off the tracked entity.</summary>
public sealed record PayslipEmployeeSnapshot(
    string FirstName, string LastName, string EmployeeNo,
    Guid DepartmentId, Guid JobTitleId, DateTime? DateOfJoining);

/// <summary>One payslip line's scalars, copied off the tracked detail entity.</summary>
public sealed record PayslipDetailSnapshot(string ComponentName, string ComponentType, decimal Amount);

/// <summary>Result of rendering + uploading one slip's PDF (the WORK phase output persisted per chunk).</summary>
public sealed record PayslipRenderOutcome(Guid SlipId, bool Ok, string? Path, int Size);

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
