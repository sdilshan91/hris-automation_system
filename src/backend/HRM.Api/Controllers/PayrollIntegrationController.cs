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
/// Tenant-scoped endpoints for attendance + leave integration into payroll (US-PAY-010). Hosts the HR-triggered
/// leave-encashment action (AC-3/FR-5) and the pre-payroll attendance reconciliation report (FR-7). The
/// attendance-finalized GATE (AC-4/FR-6) lives in the existing initiate endpoint (<see cref="PayrollRunsController"/>),
/// not here. All require <c>Payroll.Run</c> and are tenant-scoped via the EF global query filter (AC-5/FR-8).
/// </summary>
[ApiController]
[Route("api/v1/payroll")]
[Authorize]
public sealed class PayrollIntegrationController : ControllerBase
{
    private readonly IMediator _mediator;

    public PayrollIntegrationController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// POST — triggers a leave encashment for an employee (AC-3/FR-5). Adds an EARNING adjustment (Bonus) to the
    /// next payroll run = eligible_days * (monthly_basic / working_days). 422 when the leave type is not
    /// encashable (BR-6) or the employee has no active salary; 404 when the employee/leave type is unknown.
    /// </summary>
    [HttpPost("leave-encashments")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(typeof(ApiResponse<LeaveEncashmentResultDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ProcessLeaveEncashment(
        [FromBody] LeaveEncashmentRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ProcessLeaveEncashmentCommand(
            request.EmployeeId, request.LeaveTypeId, request.EligibleDays,
            request.PayMonth, request.PayYear, request.IsTaxable), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<LeaveEncashmentResultDto>.Ok(result.Value!, "Leave encashment processed."));
    }

    /// <summary>
    /// GET — the pre-payroll attendance reconciliation report for a period (FR-7): per-employee working/present/
    /// absent days, approved-leave days by type, overtime hours, and calculated LOP days. Tenant-scoped.
    /// </summary>
    [HttpGet("reconciliation")]
    [RequirePermission("Payroll.Run")]
    [ProducesResponseType(typeof(ApiResponse<PrePayrollReconciliationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetReconciliation(
        [FromQuery] int payYear, [FromQuery] int payMonth, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPrePayrollReconciliationQuery(payYear, payMonth), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<PrePayrollReconciliationDto>.Ok(result.Value!));
    }
}
