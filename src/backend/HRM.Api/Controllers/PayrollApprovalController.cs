using HRM.Application.DTOs;
using HRM.Application.Features.Payroll.Commands;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Application.Features.Payroll.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped endpoints for the payroll-run approval workflow (US-PAY-008). Implements the additive state
/// machine on top of the US-PAY-003 run lifecycle: submit (AC-1), approve (AC-2/AC-4), reject (AC-3), return
/// to HR (FR-9), finalize (AC-5), plus the approval-history (FR-7) and approval-summary (FR-4) reads.
///
/// <para>PERMISSIONS: approve / reject / return require <c>Payroll.Approve</c> (the approver gate). Submit and
/// finalize are HR/run-owner actions gated by <c>Payroll.Run</c> (consistent with the rest of the run
/// controller). The reads use <c>Payroll.Run</c> too — HR and approvers both hold it. All actions are
/// tenant-scoped via the EF global query filter (BR-8). The originating IP (FR-7) is captured from the request
/// connection and passed to the service.</para>
/// </summary>
[ApiController]
[Route("api/v1/payroll")]
[Authorize]
public sealed class PayrollApprovalController : ControllerBase
{
    private readonly IMediator _mediator;

    public PayrollApprovalController(IMediator mediator) => _mediator = mediator;

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>POST — AC-1: submit a ReviewPending run for approval (also re-submit a Rejected run, BR-3).</summary>
    [HttpPost("runs/{runId:guid}/submit-for-approval")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(typeof(ApiResponse<PayrollApprovalResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit(Guid runId, [FromBody] SubmitForApprovalRequest? request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SubmitPayrollForApprovalCommand(runId, request?.TotalApprovalSteps, request?.Comments, Ip), cancellationToken);
        return Respond(result);
    }

    /// <summary>POST — AC-2/AC-4: approve an AwaitingApproval run (advance step or mark Approved).</summary>
    [HttpPost("runs/{runId:guid}/approve")]
    [RequirePermission("Payroll.Approve")]
    [ProducesResponseType(typeof(ApiResponse<PayrollApprovalResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(Guid runId, [FromBody] PayrollApprovalActionRequest? request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ApprovePayrollRunCommand(runId, request?.Comments, Ip), cancellationToken);
        return Respond(result);
    }

    /// <summary>POST — AC-3: reject an AwaitingApproval run with a required reason.</summary>
    [HttpPost("runs/{runId:guid}/reject")]
    [RequirePermission("Payroll.Approve")]
    [ProducesResponseType(typeof(ApiResponse<PayrollApprovalResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(Guid runId, [FromBody] PayrollApprovalActionRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RejectPayrollRunCommand(runId, request?.Comments ?? string.Empty, Ip), cancellationToken);
        return Respond(result);
    }

    /// <summary>POST — FR-9: return an AwaitingApproval run to HR with required comments (no formal rejection).</summary>
    [HttpPost("runs/{runId:guid}/return")]
    [RequirePermission("Payroll.Approve")]
    [ProducesResponseType(typeof(ApiResponse<PayrollApprovalResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Return(Guid runId, [FromBody] PayrollApprovalActionRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ReturnPayrollRunToHrCommand(runId, request?.Comments ?? string.Empty, Ip), cancellationToken);
        return Respond(result);
    }

    /// <summary>POST — AC-5/BR-1/BR-6: finalize an Approved run (terminal; requires a prior approval step).</summary>
    [HttpPost("runs/{runId:guid}/finalize")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(typeof(ApiResponse<PayrollApprovalResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Finalize(Guid runId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new FinalizePayrollRunCommand(runId, Ip), cancellationToken);
        return Respond(result);
    }

    /// <summary>
    /// GET — AC-4/FR-2 (BUG-076): the tenant's configured payroll-approval step → role chain (ordered).
    /// Gated by <c>Payroll.Approve</c> — the admin-restricted approver capability (Tenant Owner / Tenant Admin
    /// only). Deliberately NOT <c>Payroll.Configure</c>, which HR Officer holds: the HR Officer is the payroll
    /// MAKER (submits/runs payroll) and is excluded from approvals for separation of duties, so letting them
    /// define who approves would undermine the very maker-checker/distinct-approver control this fixes.
    /// </summary>
    [HttpGet("approval/step-config")]
    [RequirePermission("Payroll.Approve")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PayrollApprovalStepConfigDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStepConfig(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPayrollApprovalStepConfigQuery(), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<IReadOnlyList<PayrollApprovalStepConfigDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT — AC-4/FR-2 (BUG-076): atomically REPLACE the tenant's payroll-approval step → role chain. Same
    /// <c>Payroll.Approve</c> gate as the GET (see its remarks). 400 on non-contiguous steps or a role that is
    /// unknown to the tenant / lacks Payroll.Approve.
    /// </summary>
    [HttpPut("approval/step-config")]
    [RequirePermission("Payroll.Approve")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PayrollApprovalStepConfigDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetStepConfig([FromBody] SetPayrollApprovalStepConfigRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SetPayrollApprovalStepConfigCommand(request?.Steps ?? [], Ip), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<IReadOnlyList<PayrollApprovalStepConfigDto>>.Ok(result.Value!));
    }

    /// <summary>GET — FR-7: the run's approval timeline, newest-first.</summary>
    [HttpGet("runs/{runId:guid}/approval-history")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PayrollApprovalHistoryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHistory(Guid runId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPayrollApprovalHistoryQuery(runId), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<IReadOnlyList<PayrollApprovalHistoryDto>>.Ok(result.Value!));
    }

    /// <summary>GET — FR-4: the approval-review summary (totals + previous-month variance + exceptions).</summary>
    [HttpGet("runs/{runId:guid}/approval-summary")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(typeof(ApiResponse<PayrollApprovalSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSummary(Guid runId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPayrollApprovalSummaryQuery(runId), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<PayrollApprovalSummaryDto>.Ok(result.Value!));
    }

    private IActionResult Respond(HRM.Application.Common.Models.Result<PayrollApprovalResultDto> result) =>
        result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<PayrollApprovalResultDto>.Ok(result.Value!));
}
