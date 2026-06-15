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
/// Tenant-scoped endpoints for the monthly payroll-run engine (US-PAY-003). Initiate is async (returns 202
/// Accepted with the runId while a Hangfire worker computes the slips, AC-1); the rest are reads for the runs
/// list, detail, summary, and progress (FR-6/FR-8). All require <c>Payroll.Run</c> and are tenant-scoped via
/// the EF global query filter (AC-7).
/// </summary>
[ApiController]
[Route("api/v1/payroll")]
[Authorize]
public sealed class PayrollRunsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PayrollRunsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// POST — initiates a monthly payroll run (AC-1/FR-1). Returns 202 Accepted with the runId while the
    /// Hangfire worker processes it. 409 when a non-cancelled run exists for the period (BR-1) or it is
    /// already finalized (AC-4). Accepts an optional <c>Idempotency-Key</c> header (FR-9).
    /// </summary>
    [HttpPost("runs")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(typeof(ApiResponse<PayrollRunAcceptedDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Initiate([FromBody] InitiatePayrollRunRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();

        var result = await _mediator.Send(
            new InitiatePayrollRunCommand(request.PayMonth, request.PayYear, idempotencyKey), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        // AC-1: 202 Accepted — processing continues in the background.
        return AcceptedAtAction(nameof(Get), new { runId = result.Value!.RunId },
            ApiResponse<PayrollRunAcceptedDto>.Ok(result.Value));
    }

    /// <summary>GET — lists payroll runs for the tenant, newest period first (FR-8).</summary>
    [HttpGet("runs")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PayrollRunDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListPayrollRunsQuery(), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<IReadOnlyList<PayrollRunDto>>.Ok(result.Value!));
    }

    /// <summary>GET — a single payroll run by id (FR-8).</summary>
    [HttpGet("runs/{runId:guid}")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(typeof(ApiResponse<PayrollRunDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid runId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPayrollRunQuery(runId), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<PayrollRunDto>.Ok(result.Value!));
    }

    /// <summary>GET — a run's summary totals + run log, incl. skipped-employee warnings (FR-8/AC-6).</summary>
    [HttpGet("runs/{runId:guid}/summary")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(typeof(ApiResponse<PayrollRunSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSummary(Guid runId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPayrollRunSummaryQuery(runId), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<PayrollRunSummaryDto>.Ok(result.Value!));
    }

    /// <summary>GET — a run's processed/total progress (FR-6). The FE polls this while the run is Processing.</summary>
    [HttpGet("runs/{runId:guid}/progress")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(typeof(ApiResponse<PayrollRunProgressDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProgress(Guid runId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPayrollRunProgressQuery(runId), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<PayrollRunProgressDto>.Ok(result.Value!));
    }
}
