using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>Input to initiate a payroll run (US-PAY-003 AC-1/FR-1/FR-9).</summary>
/// <param name="PayMonth">Pay period month, 1-12.</param>
/// <param name="PayYear">Pay period year.</param>
/// <param name="IdempotencyKey">Optional Idempotency-Key header value (FR-9). Null when not supplied.</param>
public sealed record InitiatePayrollRunInput(int PayMonth, int PayYear, string? IdempotencyKey);

/// <summary>
/// Payroll-run service (US-PAY-003) — the INITIATE + READ side. All operations are tenant-scoped via
/// ITenantContext + the EF global query filter (AC-7). Creates the queued run + enqueues the Hangfire
/// processing job (AC-1/FR-1/FR-2), enforces one-non-cancelled-run-per-period (BR-1) + idempotency (FR-9),
/// and exposes run list/detail/summary/progress reads (FR-6/FR-8). The actual computation lives in
/// <see cref="IPayrollRunProcessor"/>, driven by the Hangfire job.
/// </summary>
public interface IPayrollRunService
{
    /// <summary>
    /// Creates a payroll_run (status=Queued), enqueues the ProcessPayrollRunJob, and returns the runId
    /// (AC-1/FR-1/FR-2). 409 when a non-cancelled run already exists for the period (BR-1) or the period is
    /// already Finalized (AC-4); 409 + the existing run when the idempotency key was already used (FR-9).
    /// </summary>
    Task<Result<PayrollRunAcceptedDto>> InitiateAsync(InitiatePayrollRunInput input, CancellationToken cancellationToken = default);

    /// <summary>Lists payroll runs for the tenant, newest period first (FR-8, §8 table view).</summary>
    Task<Result<IReadOnlyList<PayrollRunDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets a single payroll run by id (FR-8).</summary>
    Task<Result<PayrollRunDto>> GetAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Gets a run's summary totals + run log (FR-8, AC-6 warnings).</summary>
    Task<Result<PayrollRunSummaryDto>> GetSummaryAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Gets a run's processed/total progress (FR-6). The FE polls this while the run is Processing.</summary>
    Task<Result<PayrollRunProgressDto>> GetProgressAsync(Guid runId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The compute side of the payroll engine (US-PAY-003 FR-3/FR-5). Invoked by the Hangfire
/// ProcessPayrollRunJob after it restores the tenant context. Separated from the job so the heavy compute
/// can be exercised directly in tests without a live Hangfire server. Tenant-scoped via ITenantContext +
/// the EF global query filter (AC-7).
/// </summary>
public interface IPayrollRunProcessor
{
    /// <summary>
    /// Processes a queued run end-to-end (FR-5): sets Processing, computes + persists a slip per active
    /// employee with a salary structure (skipping those without, AC-6), stamps the summary totals + counters,
    /// and moves the run to ReviewPending on completion (AC-3). Idempotent + safely re-runnable (NFR-2):
    /// re-running a ReviewPending/Cancelled run replaces its prior slips (FR-7).
    /// </summary>
    Task<Result> ProcessAsync(Guid runId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Seam that enqueues the tenant-aware ProcessPayrollRunJob (US-PAY-003 FR-2). Implemented in HRM.Api over
/// Hangfire's IBackgroundJobClient; OPTIONAL in DI — when absent (tests/dev without Hangfire storage) the
/// run service skips enqueue and the job can be invoked directly. Mirrors IInterviewReminderScheduler.
/// </summary>
public interface IPayrollRunJobScheduler
{
    /// <summary>Enqueues processing of <paramref name="runId"/> for <paramref name="tenantId"/> (FR-2/FR-3).</summary>
    string Enqueue(Guid tenantId, string tenantSubdomain, Guid runId);
}

/// <summary>
/// Notification seam for payroll-run lifecycle events (US-PAY-003 AC-3). Log-only until the notification
/// platform exists (US-NTF) — mirrors ILeaveNotificationService / IRecruitmentNotificationService. The
/// in-app SignalR push + email are DEFERRED; this records that HR should be notified the run is ready.
/// </summary>
public interface IPayrollNotificationService
{
    /// <summary>AC-3: notify HR that a run finished computing and is awaiting review.</summary>
    Task NotifyRunReadyForReviewAsync(Guid tenantId, Guid runId, int processed, int skipped, CancellationToken cancellationToken = default);
}
