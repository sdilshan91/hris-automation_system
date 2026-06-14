using HRM.Application.DTOs;
using HRM.Application.Features.Holidays.Commands;
using HRM.Application.Features.Holidays.DTOs;
using HRM.Application.Features.Holidays.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped holiday calendar endpoints (US-LV-007).
/// CRUD requires Holiday.* permissions; the list/range endpoints are readable by
/// employees (Holiday.View) so the leave-application flow and calendar view work.
/// </summary>
[ApiController]
[Route("api/v1/holidays")]
[Authorize]
public sealed class HolidaysController : ControllerBase
{
    private readonly IMediator _mediator;

    public HolidaysController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/holidays?from={date}&amp;to={date}&amp;year={year}&amp;locationId={id}&amp;activeOnly={bool}
    /// Lists holidays for the current tenant. A from/to range (FR-6) takes precedence over a
    /// year-based list (AC-4).
    /// </summary>
    [HttpGet]
    [RequirePermission("Holiday.View")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<HolidayDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHolidays(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int? year,
        [FromQuery] Guid? locationId,
        [FromQuery] bool? activeOnly,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetHolidaysQuery(from, to, year, locationId, activeOnly), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<IReadOnlyList<HolidayDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/holidays/{id}
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Holiday.View")]
    [ProducesResponseType(typeof(ApiResponse<HolidayDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHolidayById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetHolidayByIdQuery(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<HolidayDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/holidays — creates a new holiday (AC-1).
    /// </summary>
    [HttpPost]
    [RequirePermission("Holiday.Create")]
    [ProducesResponseType(typeof(ApiResponse<HolidayDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateHoliday(
        [FromBody] CreateHolidayRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateHolidayCommand(
            request.Name, request.Date, request.Type,
            request.LocationId, request.Description, request.IsRecurring);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return CreatedAtAction(
            nameof(GetHolidayById),
            new { id = result.Value!.Id },
            ApiResponse<HolidayDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/holidays/{id} — updates an existing holiday (FR-1).
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("Holiday.Edit")]
    [ProducesResponseType(typeof(ApiResponse<HolidayDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateHoliday(
        Guid id, [FromBody] UpdateHolidayRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateHolidayCommand(
            id, request.Name, request.Date, request.Type,
            request.LocationId, request.Description, request.IsRecurring);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<HolidayDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/holidays/{id}/deactivate — soft-deactivates a holiday (BR-4).
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission("Holiday.Deactivate")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateHoliday(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeactivateHolidayCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse.Ok("Holiday deactivated successfully."));
    }

    /// <summary>
    /// POST /api/v1/holidays/import — bulk-imports holidays from a CSV file (FR-4, AC-3).
    /// Columns: name, date, type. Returns a summary with created count and row-level errors.
    /// </summary>
    [HttpPost("import")]
    [RequirePermission("Holiday.Import")]
    [ProducesResponseType(typeof(ApiResponse<HolidayImportResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [RequestSizeLimit(2 * 1024 * 1024)] // CSV up to 100 rows is tiny; 2 MB is ample.
    public async Task<IActionResult> ImportHolidays(
        IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse.Fail("No file uploaded."));

        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(
            new ImportHolidaysCommand(stream, file.FileName), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<HolidayImportResult>.Ok(result.Value!));
    }
}
