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
/// Tenant-scoped bulk payslip-email distribution endpoints (US-PAY-011). Send / re-send enqueue a tenant-aware
/// Hangfire job (AC-1/FR-1/FR-4) and return 202; the summary endpoint backs the §8 distribution card the FE
/// polls (BR-6/FR-5 — SignalR live progress deferred). All require <c>Payroll.Run</c> and are tenant-scoped via
/// the EF global query filter (AC-5 — a cross-tenant run is simply not visible).
/// </summary>
[ApiController]
[Route("api/v1/payroll")]
[Authorize]
public sealed class PayslipDistributionController : ControllerBase
{
    private readonly IMediator _mediator;

    public PayslipDistributionController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// POST — sends payslip PDFs by email to all employees in a FINALIZED run (AC-1/FR-1). Returns 202 Accepted;
    /// sending continues in a Hangfire background job. 409 when the run is not Finalized
    /// (<c>run_not_finalized</c>) or payslips were already sent and <c>confirm</c> is false
    /// (<c>resend_requires_confirmation</c>, FR-7/BR-5); 400 when there are no generated PDFs.
    /// </summary>
    [HttpPost("runs/{runId:guid}/payslips/send-emails")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(typeof(ApiResponse<PayslipDistributionAcceptedDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Send(Guid runId, [FromBody] SendPayslipEmailsRequest? request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SendPayslipEmailsCommand(runId, request?.Confirm ?? false), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return AcceptedAtAction(nameof(GetSummary), new { runId },
            ApiResponse<PayslipDistributionAcceptedDto>.Ok(result.Value!,
                result.Value!.Resend ? "Payslip re-send queued." : "Payslip email distribution queued."));
    }

    /// <summary>
    /// POST — selectively re-sends payslip emails for a run (FR-4): to <c>employeeIds</c>, or (when none given
    /// and <c>onlyFailed</c> is true) to all employees whose last delivery was Failed. Returns 202 Accepted.
    /// </summary>
    [HttpPost("runs/{runId:guid}/payslips/resend-emails")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(typeof(ApiResponse<PayslipDistributionAcceptedDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Resend(Guid runId, [FromBody] ResendPayslipEmailsRequest? request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ResendPayslipEmailsCommand(runId, request?.EmployeeIds, request?.OnlyFailed ?? false), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return AcceptedAtAction(nameof(GetSummary), new { runId },
            ApiResponse<PayslipDistributionAcceptedDto>.Ok(result.Value!, "Payslip re-send queued."));
    }

    /// <summary>
    /// GET — the per-run email-distribution summary (BR-6/FR-5): total/sent/failed/skipped/queued +
    /// started/completed. The FE polls this for the §8 progress + summary card. Tenant-scoped (AC-5).
    /// </summary>
    [HttpGet("runs/{runId:guid}/payslips/distribution-summary")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(typeof(ApiResponse<PayslipDistributionSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSummary(Guid runId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPayslipDistributionSummaryQuery(runId), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<PayslipDistributionSummaryDto>.Ok(result.Value!));
    }
}
