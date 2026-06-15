using HRM.Application.DTOs;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Application.Features.Recruitment.Queries;
using HRM.Domain.Enums;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped recruiter endpoints for managing job vacancies (US-REC-001). Reads require
/// <c>Recruitment.View</c>; writes (create/update/publish/close/status) require <c>Recruitment.Manage</c>
/// — the existing catalog permissions (the Recruiter role already holds both). All operations are
/// tenant-scoped via the EF global query filter; tenant-isolation is enforced in the service layer
/// (AC-4). The anonymous public careers listing lives in <see cref="CareersController"/>.
/// </summary>
[ApiController]
[Route("api/v1/recruitment/vacancies")]
[Authorize]
public sealed class VacanciesController : ControllerBase
{
    private readonly IMediator _mediator;

    public VacanciesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/recruitment/vacancies
    /// Lists vacancies for the current tenant, paged and filterable by status / department / free-text
    /// search (NFR-1). Returns the contract shape { data, total, page, pageSize }.
    /// </summary>
    [HttpGet]
    [RequirePermission("Recruitment.View")]
    [ProducesResponseType(typeof(ApiResponse<VacancyPageResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] VacancyStatus? status,
        [FromQuery] Guid? departmentId,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new ListVacanciesQuery(status, departmentId, search, page, pageSize), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var paged = result.Value!;
        var response = new VacancyPageResponse
        {
            Data = paged.Items,
            Total = paged.TotalCount,
            Page = paged.Page,
            PageSize = paged.PageSize,
        };

        return Ok(ApiResponse<VacancyPageResponse>.Ok(response));
    }

    /// <summary>
    /// GET /api/v1/recruitment/vacancies/{id}
    /// Gets a single vacancy by id (tenant-scoped).
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Recruitment.View")]
    [ProducesResponseType(typeof(ApiResponse<VacancyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetVacancyByIdQuery(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<VacancyDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/recruitment/vacancies
    /// Creates a vacancy in Draft status (AC-1).
    /// </summary>
    [HttpPost]
    [RequirePermission("Recruitment.Manage")]
    [ProducesResponseType(typeof(ApiResponse<VacancyDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateVacancyRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateVacancyCommand(
            request.Title, request.DepartmentId, request.JobTitleId, request.LocationId,
            request.HiringManagerId, request.EmploymentType, request.Headcount,
            request.SalaryMin, request.SalaryMax, request.SalaryCurrency,
            request.Description, request.Qualifications, request.ApplicationDeadline,
            request.PublishToPublicCareers);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Value!.Id },
            ApiResponse<VacancyDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/recruitment/vacancies/{id}
    /// Updates an existing vacancy (AC-3).
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("Recruitment.Manage")]
    [ProducesResponseType(typeof(ApiResponse<VacancyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateVacancyRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateVacancyCommand(
            id, request.Title, request.DepartmentId, request.JobTitleId, request.LocationId,
            request.HiringManagerId, request.EmploymentType, request.Headcount,
            request.SalaryMin, request.SalaryMax, request.SalaryCurrency,
            request.Description, request.Qualifications, request.ApplicationDeadline,
            request.PublishToPublicCareers);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<VacancyDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/recruitment/vacancies/{id}/publish
    /// Publishes a Draft vacancy (Draft → Open, AC-2). Enforces BR-2 required fields + generates the slug.
    /// </summary>
    [HttpPost("{id:guid}/publish")]
    [RequirePermission("Recruitment.Manage")]
    [ProducesResponseType(typeof(ApiResponse<VacancyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new PublishVacancyCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<VacancyDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/recruitment/vacancies/{id}/close
    /// Closes an Open/OnHold vacancy (AC-5). Existing applicants keep their stage (BR-3).
    /// </summary>
    [HttpPost("{id:guid}/close")]
    [RequirePermission("Recruitment.Manage")]
    [ProducesResponseType(typeof(ApiResponse<VacancyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Close(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CloseVacancyCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<VacancyDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/recruitment/vacancies/{id}/status
    /// Changes a vacancy's status with FR-2 transition validation. Body: { status }.
    /// </summary>
    [HttpPost("{id:guid}/status")]
    [RequirePermission("Recruitment.Manage")]
    [ProducesResponseType(typeof(ApiResponse<VacancyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeStatus(
        Guid id, [FromBody] ChangeVacancyStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ChangeVacancyStatusCommand(id, request.Status), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<VacancyDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/recruitment/vacancies/status
    /// Bulk status change for multiple vacancies (FR-6); returns per-item partial results.
    /// </summary>
    [HttpPost("status")]
    [RequirePermission("Recruitment.Manage")]
    [ProducesResponseType(typeof(ApiResponse<BulkStatusChangeResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BulkChangeStatus(
        [FromBody] BulkChangeVacancyStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new BulkChangeVacancyStatusCommand(request.VacancyIds, request.Status), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<BulkStatusChangeResult>.Ok(result.Value!));
    }
}

/// <summary>
/// Frontend list-response contract: { data, total, page, pageSize }. A thin projection over the
/// internal <see cref="PagedResult{T}"/> so the API surface matches what the recruitment UI consumes.
/// </summary>
public sealed record VacancyPageResponse
{
    public IReadOnlyList<VacancyListItemDto> Data { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}
