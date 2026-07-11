using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Bulk payslip-email distribution (US-PAY-011). The ENQUEUE + SUMMARY side; the heavy per-employee send loop
/// lives in <see cref="IPayslipDistributionRunner"/> (invoked by the Hangfire SendPayslipEmailsJob after it
/// restores the tenant context, or directly in tests). All operations are tenant-scoped via ITenantContext +
/// the EF global query filter (AC-5).
/// </summary>
public interface IPayslipDistributionService
{
    /// <summary>
    /// AC-1/FR-1: enqueues the SendPayslipEmailsJob for a FINALIZED run whose PDFs are generated (BR-1) and
    /// returns 202. FR-7/BR-5 duplicate guard: if payslips were already sent for the run and
    /// <paramref name="confirmResend"/> is false, fails with 409 <c>resend_requires_confirmation</c>.
    /// </summary>
    Task<Result<PayslipDistributionAcceptedDto>> SendAsync(
        Guid runId, bool confirmResend, CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-4: selective re-send. Re-enqueues the job targeting either an explicit set of employees, or (when
    /// <paramref name="employeeIds"/> is null/empty and <paramref name="onlyFailed"/> is true) all employees
    /// whose last delivery for the run was Failed. Implies confirmation (HR explicitly chose the targets).
    /// </summary>
    Task<Result<PayslipDistributionAcceptedDto>> ResendAsync(
        Guid runId, IReadOnlyCollection<Guid>? employeeIds, bool onlyFailed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// BR-6/FR-5: the per-run distribution summary (total/sent/failed/skipped/queued + started/completed),
    /// for the §8 summary card the FE polls (SignalR deferred).
    /// </summary>
    Task<Result<PayslipDistributionSummaryDto>> GetSummaryAsync(
        Guid runId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The compute side of payslip-email distribution (US-PAY-011 FR-2/FR-8). Invoked by the Hangfire
/// SendPayslipEmailsJob after it restores the tenant context (so the global query filter scopes to the run's
/// tenant — AC-5), or directly in tests. Iterates the run's employees, loads each PDF from
/// <see cref="IFileStorage"/>, sends one email via <see cref="IPayslipEmailSender"/> with Polly retry
/// (NFR-2), and writes a <c>PayslipEmailLog</c> row per employee. Idempotent: skips employees already Sent
/// (NFR-3); employees with no email are Skipped + a warning, the loop continues (AC-3).
/// </summary>
public interface IPayslipDistributionRunner
{
    /// <summary>
    /// Sends payslip emails for the run. When <paramref name="targetEmployeeIds"/> is null, processes every
    /// employee in the run (skipping those already Sent, NFR-3); otherwise only the targeted employees (FR-4
    /// selective re-send). Returns the resulting summary. Thin convenience that runs the three phases below in
    /// sequence for non-job callers (tests) — the Hangfire job orchestrates the phases so each SMTP send runs
    /// OUTSIDE any tenant-GUC transaction and each result is committed per-send (ISSUE-269).
    /// </summary>
    Task<Result<PayslipDistributionSummaryDto>> RunAsync(
        Guid runId, IReadOnlyCollection<Guid>? targetEmployeeIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// ISSUE-269 phase 1 — READ. Loads (AsNoTracking) the run + slips + employees + already-Sent set and projects
    /// them to non-tracked send-items. Keeps the BR-1 Finalized guard (409). Runs inside a short tenant-GUC
    /// transaction (the job wraps it in <see cref="ITenantJobRunner"/>). 404 when the run does not exist.
    /// </summary>
    Task<Result<PayslipSendPlan>> LoadSendPlanAsync(
        Guid runId, IReadOnlyCollection<Guid>? targetEmployeeIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// ISSUE-269 phase 2 — WORK (no DB, no transaction). Reads the PDF from storage and sends ONE email with the
    /// existing Polly retry (NFR-2); the job runs it OUTSIDE any runner call so SMTP is never inside a tenant-GUC
    /// transaction. Preserves the AC-3 no-email → Skipped and no-PDF → Failed branches. Returns the send outcome.
    /// A storage/infra fault propagates (a real mid-batch crash) — already-persisted sends survive it (phase 3).
    /// </summary>
    Task<PayslipSendOutcome> SendOneAsync(PayslipSendItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// ISSUE-269 phase 3 — WRITE (one short transaction per send). Upserts the (run, employee) PayslipEmailLog row
    /// for this outcome, SaveChanges, then clears the change tracker. Committing per-send is what makes a partial
    /// run resumable: a crash after this returns cannot lose an already-recorded send.
    /// </summary>
    Task PersistSendOutcomeAsync(PayslipSendOutcome outcome, CancellationToken cancellationToken = default);
}

/// <summary>
/// ISSUE-269: an immutable snapshot of everything the per-send WORK phase needs — NO tracked entities, so it can
/// safely outlive the READ transaction and be processed with no DbContext access.
/// </summary>
public sealed record PayslipSendPlan(
    Guid RunId,
    Guid TenantId,
    string CompanyName,
    string? FromAddress,
    IReadOnlyList<PayslipSendItem> Items,
    IReadOnlySet<Guid> AlreadySent);

/// <summary>One employee's non-tracked send inputs.</summary>
public sealed record PayslipSendItem(
    Guid RunId,
    Guid TenantId,
    Guid PayrollSlipId,
    Guid EmployeeId,
    string? RecipientEmail,
    string EmployeeName,
    string EmployeeNo,
    int PayMonth,
    int PayYear,
    string CompanyName,
    string? FromAddress,
    bool PdfGenerated,
    string? PdfStoragePath,
    int RetryCountBaseline);

/// <summary>Result of attempting one employee's send (persisted per-send in phase 3).</summary>
public sealed record PayslipSendOutcome(
    Guid RunId,
    Guid PayrollSlipId,
    Guid EmployeeId,
    string Recipient,
    string Status,
    DateTime? SentAt,
    string? FailureReason,
    int RetryCount);

/// <summary>
/// Seam that enqueues the tenant-aware SendPayslipEmailsJob (US-PAY-011 FR-1/FR-8). Implemented in HRM.Api
/// over Hangfire's IBackgroundJobClient; OPTIONAL in DI — when absent (tests/dev without Hangfire storage) the
/// distribution service skips enqueue and the runner is invoked directly. Mirrors
/// <see cref="IPayslipGenerationJobScheduler"/>.
/// </summary>
public interface IPayslipDistributionJobScheduler
{
    /// <summary>
    /// Enqueues payslip-email distribution of <paramref name="runId"/> for <paramref name="tenantId"/>. A null
    /// <paramref name="targetEmployeeIds"/> means "all employees in the run"; a non-null set is a selective
    /// re-send (FR-4).
    /// </summary>
    string Enqueue(Guid tenantId, string tenantSubdomain, Guid runId, IReadOnlyCollection<Guid>? targetEmployeeIds);
}
