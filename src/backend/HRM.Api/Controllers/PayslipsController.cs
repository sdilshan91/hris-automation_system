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
/// Tenant-scoped payslip-PDF endpoints (US-PAY-004). Generate/regenerate enqueue a Hangfire batch job
/// (AC-1/AC-5/FR-4) and return 202; the rest are status reads (FR-7) and streamed downloads — one PDF or a
/// ZIP of the whole run (FR-6/AC-3). All require <c>Payroll.Run</c> and are tenant-scoped via the EF global
/// query filter (AC-4 — a cross-tenant payslip is simply not visible, so download returns 404).
/// </summary>
[ApiController]
[Route("api/v1/payroll")]
[Authorize]
public sealed class PayslipsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PayslipsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// POST — generates (or regenerates, AC-5) the PDF payslips for a run (AC-1/FR-4). Returns 202 Accepted;
    /// rendering continues in a Hangfire background job. 400 when the run is not ReviewPending/Approved/
    /// Finalized (BR-1) or has no slips.
    /// </summary>
    [HttpPost("runs/{runId:guid}/payslips/generate")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(typeof(ApiResponse<PayslipGenerationAcceptedDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Generate(Guid runId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GeneratePayslipsCommand(runId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return AcceptedAtAction(nameof(GetStatus), new { runId },
            ApiResponse<PayslipGenerationAcceptedDto>.Ok(result.Value!,
                result.Value!.Regenerated ? "Payslip regeneration queued." : "Payslip generation queued."));
    }

    /// <summary>
    /// POST — regenerates (overwrites) the PDF payslips for a run (AC-5/FR-4). Returns 202 Accepted. Same
    /// BR-1 status guard as generate; behaviourally a re-run that resets every slip to Pending + re-enqueues.
    /// </summary>
    [HttpPost("runs/{runId:guid}/payslips/regenerate")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(typeof(ApiResponse<PayslipGenerationAcceptedDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Regenerate(Guid runId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RegeneratePayslipsCommand(runId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return AcceptedAtAction(nameof(GetStatus), new { runId },
            ApiResponse<PayslipGenerationAcceptedDto>.Ok(result.Value!, "Payslip regeneration queued."));
    }

    /// <summary>
    /// POST — retries the PDF render of ONE slip in the run (FR-8 / DF-31 / ISSUE-162). PDF re-render only (no
    /// payroll recalc). Returns 202 Accepted; the single-slip render continues in a Hangfire background job. Same
    /// BR-1 status guard as generate (400 <c>run_not_ready_for_payslips</c>; Finalized runs are allowed — the
    /// primary use case). 404 <c>payslip_not_found</c> when the slip is not visible for the tenant (AC-4).
    /// </summary>
    [HttpPost("runs/{runId:guid}/payslips/{employeeId:guid}/retry")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(typeof(ApiResponse<PayslipGenerationAcceptedDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Retry(Guid runId, Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RetryPayslipCommand(runId, employeeId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return AcceptedAtAction(nameof(GetStatus), new { runId },
            ApiResponse<PayslipGenerationAcceptedDto>.Ok(result.Value!, "Payslip retry queued."));
    }

    /// <summary>
    /// GET — lists the run's payslips for the §8 Notion-style table (employee name/no/department, net salary,
    /// PDF status). Tenant-scoped (AC-4). Returns an empty array when the run has no slips.
    /// </summary>
    [HttpGet("runs/{runId:guid}/payslips")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PayslipListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid runId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListRunPayslipsQuery(runId), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<IReadOnlyList<PayslipListItemDto>>.Ok(result.Value!));
    }

    /// <summary>GET — per-status counts for the run's payslip PDFs (FR-7, §8 status bar).</summary>
    [HttpGet("runs/{runId:guid}/payslips/status")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(typeof(ApiResponse<PayslipGenerationStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid runId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPayslipGenerationStatusQuery(runId), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<PayslipGenerationStatusDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET — streams ONE employee's payslip PDF (FR-6). Tenant-scoped (AC-4): a cross-tenant payslip is
    /// invisible and returns 404. File name is BR-5 <c>{EmployeeNo}_{PayMonth}_{PayYear}.pdf</c>.
    /// </summary>
    [HttpGet("runs/{runId:guid}/payslips/{employeeId:guid}/download")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadOne(Guid runId, Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DownloadPayslipQuery(runId, employeeId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var file = result.Value!;
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>
    /// GET — bulk download: a ZIP of all generated payslip PDFs in the run (FR-6/AC-3). 404 when the run has
    /// no generated payslips.
    /// </summary>
    [HttpGet("runs/{runId:guid}/payslips/download-all")]
    [HttpGet("runs/{runId:guid}/payslips/download-zip")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadAll(Guid runId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DownloadAllPayslipsQuery(runId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var file = result.Value!;
        return File(file.Content, file.ContentType, file.FileName);
    }
}
