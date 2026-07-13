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

    /// <summary>
    /// US-PAY-010 FR-7: the pre-payroll attendance reconciliation report for a period — per-employee working/
    /// present/absent days, approved-leave days broken down by type, overtime hours, and calculated LOP days.
    /// Reuses the US-ATT-009 attendance pull (LOP/overtime) + the leave module (leave-by-type) and surfaces
    /// whether attendance is finalized (the AC-4 banner driver). Tenant-scoped via the EF global query filter.
    /// </summary>
    Task<Result<PrePayrollReconciliationDto>> GetPrePayrollReconciliationAsync(
        int payYear, int payMonth, CancellationToken cancellationToken = default);

    /// <summary>
    /// ISSUE-154: cancels a payroll run before finalization. Cleans up — removes the run's payslips + reverts
    /// its Applied adjustments back to Pending (via the shared <see cref="IPayrollSlipCleaner"/>, the SAME
    /// cleanup the re-run path uses) — then sets status <see cref="HRM.Domain.Enums.PayrollRunStatus.Cancelled"/>.
    /// The freed partial-unique-index slot lets HR initiate a fresh run for the same period. Does NOT touch the
    /// US-ATT-009 attendance period lock (payroll never holds it). 404 when missing; 409 <c>run_finalized</c>
    /// for a finalized run (immutable, BR-7); 409 <c>run_already_cancelled</c> when already cancelled; 409
    /// <c>run_in_progress</c> for a run actively being computed (Processing) — it cannot be cancelled mid-flight
    /// (no concurrency token; the in-flight job would flip it back to ReviewPending), so wait until it is ready
    /// for review. A Queued run stays cancellable — its enqueued job hits the ProcessAsync start-guard.
    /// </summary>
    Task<Result<PayrollRunAcceptedDto>> CancelAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ISSUE-154: re-runs a processed-but-not-approved run IN PLACE by re-enqueuing the processing job (which
    /// re-invokes <see cref="IPayrollRunProcessor.ProcessAsync"/> — its own cleanup replaces the prior slips,
    /// FR-7). Restricted to <see cref="HRM.Domain.Enums.PayrollRunStatus.ReviewPending"/> for safety. 404 when
    /// missing; 409 <c>run_finalized</c> (immutable); 409 <c>run_in_progress</c> for a Queued/Processing run;
    /// 409 <c>run_cancelled</c> (initiate a new run instead); 409 <c>run_not_rerunnable</c> otherwise.
    /// </summary>
    Task<Result<PayrollRunAcceptedDto>> RerunAsync(Guid runId, CancellationToken cancellationToken = default);
}

/// <summary>
/// ISSUE-154: the single, shared slip-cleanup used by BOTH the re-run path
/// (<see cref="IPayrollRunProcessor.ProcessAsync"/>) and the cancel path
/// (<see cref="IPayrollRunService.CancelAsync"/>). Hard-removes a run's PayrollSlip + PayrollSlipDetail rows
/// and reverts the adjustments this run had marked Applied back to Pending (so a re-run re-picks them, or a
/// cancel releases them), committing via its own SaveChanges. Tenant-scoped via the EF global query filter.
/// Extracted from the former private <c>PayrollRunProcessor.RemoveExistingSlipsAsync</c> so there is ONE
/// implementation of the deletion + adjustment-revert logic.
/// </summary>
public interface IPayrollSlipCleaner
{
    /// <summary>Removes <paramref name="runId"/>'s slips + details and reverts its Applied adjustments to Pending.</summary>
    Task RemoveRunSlipsAndRevertAdjustmentsAsync(Guid runId, CancellationToken cancellationToken = default);
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
    /// re-running a ReviewPending run replaces its prior slips (FR-7). Finalized and Cancelled runs are
    /// terminal — the processor bails out rather than reprocessing them (BR-7 / ISSUE-154).
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

    /// <summary>US-PAY-008 AC-1: notify the designated approver(s) that a run was submitted for approval.</summary>
    Task NotifyApprovalEventAsync(Guid tenantId, Guid runId, string eventType, CancellationToken cancellationToken = default);
}
