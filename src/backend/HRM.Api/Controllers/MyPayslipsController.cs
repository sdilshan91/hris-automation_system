using HRM.Application.DTOs;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Application.Features.Payroll.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Employee self-service payslip endpoints (US-PAY-005). Every action is gated by <c>Payroll.View.Own</c>
/// (the registered self-scope permission granted to the Employee role — the story names "Payroll.Read.Self"
/// but that string is not in the catalog; the established self convention is <c>Module.View.Own</c>, cf.
/// <c>Employee.View.Own</c> / <c>Leave.View.Own</c> / <c>Attendance.View.Own</c>). The service resolves the
/// caller's employee_id from the JWT and scopes every read to it: a request for another employee's payslip id
/// returns 403 (AC-4), never another employee's data (BR-5). Only Finalized-run payslips are accessible (BR-1).
/// </summary>
[ApiController]
[Route("api/v1/payroll/my-payslips")]
[Authorize]
public sealed class MyPayslipsController : ControllerBase
{
    private readonly IMediator _mediator;

    public MyPayslipsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// GET — lists the authenticated employee's own payslips from Finalized runs (AC-1/FR-2), most recent
    /// first, paginated (FR-6 default 12/page, optional <paramref name="year"/> filter). 403 when no employee
    /// record is linked to the current user.
    /// </summary>
    [HttpGet]
    [RequirePermission("Payroll.View.Own")]
    [ProducesResponseType(typeof(ApiResponse<MyPayslipListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] int? year,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetMyPayslipsQuery(year, page, pageSize), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<MyPayslipListDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET — full earnings/deductions breakdown for ONE of the caller's own payslips (AC-2/FR-3). 403 when the
    /// payslip belongs to another employee (AC-4 — URL manipulation) or its run is not Finalized (BR-1); 404
    /// when no such payslip exists for the tenant.
    /// </summary>
    [HttpGet("{payslipId:guid}")]
    [RequirePermission("Payroll.View.Own")]
    [ProducesResponseType(typeof(ApiResponse<MyPayslipDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detail(Guid payslipId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyPayslipDetailQuery(payslipId), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<MyPayslipDetailDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET — streams the pre-generated tenant-branded PDF for ONE of the caller's own payslips (AC-3/FR-4).
    /// Same authz as the detail endpoint: 403 for another employee's payslip (AC-4), 404 when the PDF has not
    /// been generated yet.
    /// </summary>
    [HttpGet("{payslipId:guid}/pdf")]
    [RequirePermission("Payroll.View.Own")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadPdf(Guid payslipId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DownloadMyPayslipQuery(payslipId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var file = result.Value!;
        return File(file.Content, file.ContentType, file.FileName);
    }
}
