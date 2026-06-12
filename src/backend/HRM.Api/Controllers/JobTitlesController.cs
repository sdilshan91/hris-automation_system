using HRM.Application.DTOs;
using HRM.Application.Features.JobTitles.Commands;
using HRM.Application.Features.JobTitles.DTOs;
using HRM.Application.Features.JobTitles.Queries;
using HRM.Domain.Enums;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped job title management endpoints (US-CHR-005).
/// Requires JobTitle.View or JobTitle.Create/Edit/Deactivate permissions.
/// </summary>
[ApiController]
[Route("api/v1/tenant/job-titles")]
[Authorize]
public sealed class JobTitlesController : ControllerBase
{
    private readonly IMediator _mediator;

    public JobTitlesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/tenant/job-titles
    /// Lists all job titles for the current tenant.
    /// Optional query parameter: activeOnly (bool).
    /// </summary>
    [HttpGet]
    [RequirePermission("JobTitle.View")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<JobTitleDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetJobTitles(
        [FromQuery] bool? activeOnly, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetJobTitlesQuery(activeOnly), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<IReadOnlyList<JobTitleDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/job-titles/{id}
    /// Gets a single job title by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("JobTitle.View")]
    [ProducesResponseType(typeof(ApiResponse<JobTitleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetJobTitleById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetJobTitleByIdQuery(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<JobTitleDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/job-titles
    /// Creates a new job title (AC-2).
    /// </summary>
    [HttpPost]
    [RequirePermission("JobTitle.Create")]
    [ProducesResponseType(typeof(ApiResponse<JobTitleDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateJobTitle(
        [FromBody] CreateJobTitleRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateJobTitleCommand(
            request.TitleName, request.Description, request.GradeId);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return CreatedAtAction(
            nameof(GetJobTitleById),
            new { id = result.Value!.Id },
            ApiResponse<JobTitleDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/tenant/job-titles/{id}
    /// Updates an existing job title.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("JobTitle.Edit")]
    [ProducesResponseType(typeof(ApiResponse<JobTitleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateJobTitle(
        Guid id, [FromBody] UpdateJobTitleRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateJobTitleCommand(
            id, request.TitleName, request.Description, request.GradeId);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<JobTitleDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/job-titles/{id}/deactivate
    /// Deactivates (soft-deletes) a job title (AC-5, FR-5, FR-7).
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission("JobTitle.Deactivate")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateJobTitle(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeactivateJobTitleCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse.Ok("Job title deactivated successfully."));
    }

    /// <summary>
    /// GET /api/v1/tenant/job-titles/employment-types
    /// Returns the list of available employment types (FR-6).
    /// Phase 1: enum-based reference list, not a full entity.
    /// </summary>
    [HttpGet("employment-types")]
    [RequirePermission("JobTitle.View")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EmploymentTypeDto>>), StatusCodes.Status200OK)]
    public IActionResult GetEmploymentTypes()
    {
        var types = Enum.GetValues<EmploymentType>()
            .Select(e => new EmploymentTypeDto
            {
                Id = (int)e,
                Name = e.ToString(),
                DisplayName = FormatDisplayName(e),
            })
            .ToList();

        return Ok(ApiResponse<IReadOnlyList<EmploymentTypeDto>>.Ok(types));
    }

    // ── Private helpers ──────────────────────────────────────────────

    private static string FormatDisplayName(EmploymentType type) => type switch
    {
        EmploymentType.FullTime => "Full-Time",
        EmploymentType.PartTime => "Part-Time",
        EmploymentType.Contract => "Contract",
        EmploymentType.Intern => "Intern",
        _ => type.ToString(),
    };
}
