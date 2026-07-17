using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// The payroll-run approval workflow (US-PAY-008). Implements the additive state machine on top of the
/// existing US-PAY-003 run lifecycle (BR-4): ReviewPending -&gt; AwaitingApproval -&gt; Approved -&gt;
/// Finalized, plus AwaitingApproval -&gt; Rejected -&gt; ReviewPending (re-submit, BR-3) and a ReturnToHR
/// shortcut (FR-9). No shared approval-workflow engine exists (technical doc section 34 / US-ADM-007 is not
/// built), so this is a payroll-specific flow mirroring the leave/attendance approval-history pattern.
///
/// <para>All operations are tenant-scoped via ITenantContext + the EF global query filter (BR-8). Each action
/// writes an immutable <see cref="HRM.Domain.Entities.PayrollApprovalHistory"/> row (FR-7) and notifies via
/// the log-only <see cref="IPayrollNotificationService"/> seam (real SignalR/email deferred). The optional
/// originating IP (FR-7) is passed by the controller from the request context; null when unavailable.</para>
/// </summary>
public interface IPayrollApprovalService
{
    /// <summary>
    /// AC-1: ReviewPending -&gt; AwaitingApproval. Starts a new workflow instance, records a Submitted history
    /// row, notifies the approver. Optionally configures multi-step routing (AC-4); defaults to a single step.
    /// </summary>
    Task<Result<PayrollApprovalResultDto>> SubmitForApprovalAsync(
        Guid runId, int? totalApprovalSteps, string? comments, string? ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-2: AwaitingApproval -&gt; (advance step, or Approved when all steps complete, AC-4). Enforces
    /// maker-checker (BR-5): the submitter cannot approve their own run unless the tenant has fewer than 2
    /// eligible approvers (users whose roles grant Payroll.Approve).
    /// </summary>
    Task<Result<PayrollApprovalResultDto>> ApproveAsync(
        Guid runId, string? comments, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-3: AwaitingApproval -&gt; Rejected (reason required). Records the reason, notifies HR. HR corrects
    /// and re-submits (a new workflow instance, BR-3).
    /// </summary>
    Task<Result<PayrollApprovalResultDto>> RejectAsync(
        Guid runId, string reason, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-9: AwaitingApproval -&gt; ReviewPending without a formal rejection — send back to HR with comments
    /// (required). Closes the active workflow instance; a subsequent submit starts a new instance.
    /// </summary>
    Task<Result<PayrollApprovalResultDto>> ReturnToHrAsync(
        Guid runId, string comments, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-5/BR-1/BR-6: Approved -&gt; Finalized (terminal, irreversible). Requires at least one approval step
    /// first (a direct ReviewPending -&gt; Finalized is blocked, BR-1). Locks payslips immutable (FR-8).
    /// </summary>
    Task<Result<PayrollApprovalResultDto>> FinalizeAsync(
        Guid runId, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>FR-7/§8: the run's approval timeline, newest-first.</summary>
    Task<Result<IReadOnlyList<PayrollApprovalHistoryDto>>> GetHistoryAsync(
        Guid runId, CancellationToken cancellationToken = default);

    /// <summary>FR-4: the approval-review summary (totals + previous-month variance + exceptions).</summary>
    Task<Result<PayrollApprovalSummaryDto>> GetSummaryAsync(
        Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-4/FR-2 (BUG-076): the tenant's configured payroll-approval step → role bindings, ordered by step.
    /// Empty when the tenant has not configured a chain (legacy caller-supplied step count is then used).
    /// </summary>
    Task<Result<IReadOnlyList<PayrollApprovalStepConfigDto>>> GetApprovalStepConfigAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-4/FR-2 (BUG-076): atomically REPLACE the tenant's payroll-approval step → role config. Validates the
    /// steps are contiguous 1..N and each role exists in the tenant AND holds <c>Payroll.Approve</c> (else 400),
    /// then swaps the config and writes an audit entry. Returns the persisted config (ordered).
    /// </summary>
    Task<Result<IReadOnlyList<PayrollApprovalStepConfigDto>>> SetApprovalStepConfigAsync(
        IReadOnlyList<PayrollApprovalStepConfigItem> steps, string? ipAddress,
        CancellationToken cancellationToken = default);
}
