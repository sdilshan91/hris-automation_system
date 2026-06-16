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
/// Tenant-scoped payroll-adjustment endpoints (US-PAY-007). HR Officers create ad-hoc bonuses, deductions,
/// reimbursements, and corrections that the payroll-run engine (US-PAY-003) picks up as payslip line items
/// (FR-3). All require <c>Payroll.Configure</c> and are tenant-scoped via the EF global query filter (AC-5 —
/// a cross-tenant adjustment is simply not visible, so reads return 404). Supporting documents
/// (PDF/JPG/PNG &lt;= 5 MB) are stored in tenant blob storage (AC-3/NFR-5).
/// </summary>
[ApiController]
[Route("api/v1/payroll/adjustments")]
[Authorize]
public sealed class PayrollAdjustmentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PayrollAdjustmentsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// GET — lists adjustments with §8 filters (status/type/period/employee), most-recent-first, paginated.
    /// </summary>
    [HttpGet]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<PayrollAdjustmentPageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? adjustmentType,
        [FromQuery] int? payMonth,
        [FromQuery] int? payYear,
        [FromQuery] Guid? employeeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new ListPayrollAdjustmentsQuery(status, adjustmentType, payMonth, payYear, employeeId, page, pageSize),
            cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<PayrollAdjustmentPageDto>.Ok(result.Value!));
    }

    /// <summary>GET — a single adjustment by id (tenant-scoped; cross-tenant → 404).</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<PayrollAdjustmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPayrollAdjustmentQuery(id), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<PayrollAdjustmentDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST — creates an adjustment (FR-1). Recurring adjustments auto-create the future Pending series
    /// (FR-5/BR-6). The result carries a BR-3 negative-net warning and any BR-7/BR-8 period deferral.
    /// </summary>
    [HttpPost]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<CreatePayrollAdjustmentResult>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreatePayrollAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreatePayrollAdjustmentCommand(
            request.EmployeeId, request.AdjustmentType, request.Amount, request.Description,
            request.ApplicablePayMonth, request.ApplicablePayYear, request.IsTaxable, request.IsRecurring,
            request.RecurrenceEndMonth, request.RecurrenceEndYear, request.ReferencePayrollSlipId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<CreatePayrollAdjustmentResult>.Ok(result.Value!, "Adjustment created."));
    }

    /// <summary>POST — cancels a Pending adjustment (FR-6). Applied/Cancelled → 409.</summary>
    [HttpPost("{id:guid}/cancel")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CancelPayrollAdjustmentCommand(id), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse.Ok("Adjustment cancelled."));
    }

    /// <summary>
    /// POST (multipart/form-data) — bulk-creates adjustments from a CSV for one period (FR-2). CSV header:
    /// employee_no, adjustment_type, amount, description, is_taxable. Returns per-row results.
    /// </summary>
    [HttpPost("bulk")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<BulkAdjustmentResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BulkUpload(
        IFormFile file,
        [FromForm] int payMonth,
        [FromForm] int payYear,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse.Fail("A CSV file is required.", "file_required"));

        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new BulkCreatePayrollAdjustmentsCommand(payMonth, payYear, stream), cancellationToken);

        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<BulkAdjustmentResultDto>.Ok(result.Value!,
                $"{result.Value!.SucceededCount} of {result.Value.TotalRows} rows created."));
    }

    /// <summary>
    /// POST (multipart/form-data) — uploads a supporting document for an adjustment (AC-3/NFR-5). Validates
    /// type (PDF/JPG/PNG) and size (&lt;= 5 MB). Stored at {tenantId}/payroll/adjustments/{adjustmentId}/.
    /// </summary>
    [HttpPost("{id:guid}/document")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadDocument(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse.Fail("A document file is required.", "file_required"));

        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(
            new UploadAdjustmentDocumentCommand(id, stream, file.FileName, file.ContentType, file.Length),
            cancellationToken);

        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<string>.Ok(result.Value!, "Document uploaded."));
    }

    /// <summary>GET — streams an adjustment's supporting document (AC-3). Missing/cross-tenant → 404.</summary>
    [HttpGet("{id:guid}/document")]
    [RequirePermission("Payroll.Configure")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadDocument(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DownloadAdjustmentDocumentQuery(id), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var doc = result.Value!;
        return File(doc.Content, doc.ContentType, doc.FileName);
    }
}

/// <summary>Request body for creating a payroll adjustment (US-PAY-007 FR-1).</summary>
public sealed record CreatePayrollAdjustmentRequest(
    Guid EmployeeId,
    string AdjustmentType,
    decimal Amount,
    string Description,
    int ApplicablePayMonth,
    int ApplicablePayYear,
    bool IsTaxable,
    bool IsRecurring,
    int? RecurrenceEndMonth,
    int? RecurrenceEndYear,
    Guid? ReferencePayrollSlipId);
