using HRM.Application.DTOs;
using HRM.Application.Features.Onboarding.Commands;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Application.Features.Onboarding.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped HR endpoints for managing onboarding checklist templates (US-ONB-001). Reads require
/// <c>Onboarding.View</c>; writes (create/clone/activate/deactivate) require <c>Onboarding.Manage</c>.
/// All operations are tenant-scoped via the EF global query filter; tenant isolation is enforced in the
/// service layer (AC-5). The tenant_id is always taken from the resolved session context, never from
/// user input (FR-5).
/// </summary>
[ApiController]
[Route("api/v1/onboarding/templates")]
[Authorize]
public sealed class OnboardingTemplatesController : ControllerBase
{
    private readonly IMediator _mediator;

    public OnboardingTemplatesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/onboarding/templates
    /// Lists checklist templates for the current tenant, paged and filterable by active state + name search.
    /// </summary>
    [HttpGet]
    [RequirePermission("Onboarding.View")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<OnboardingTemplateListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] bool? isActive,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetTemplatesQuery(isActive, search, page, pageSize), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<PagedResult<OnboardingTemplateListItemDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/onboarding/templates/{id}
    /// Gets a single checklist template (with its tasks) by id, tenant-scoped.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Onboarding.View")]
    [ProducesResponseType(typeof(ApiResponse<OnboardingTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTemplateByIdQuery(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<OnboardingTemplateDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/onboarding/templates
    /// Creates a checklist template with its tasks (AC-1/AC-2). Rejects a duplicate name (AC-3/BR-1) and
    /// an empty task list (BR-2).
    /// </summary>
    [HttpPost]
    [RequirePermission("Onboarding.Manage")]
    [ProducesResponseType(typeof(ApiResponse<OnboardingTemplateDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateOnboardingTemplateRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateChecklistTemplateCommand(
            request.TemplateName,
            request.Description,
            request.ApplicableDepartments,
            request.ApplicableJobTitles,
            request.IsActive,
            request.Tasks.Select(t => new CreateChecklistTemplateTask(
                t.Title, t.Description, t.Category, t.ResponsibleRole, t.ResponsibleUserId,
                t.DueOffsetDays, t.IsMandatory, t.SortOrder)).ToList());

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Value!.Id },
            ApiResponse<OnboardingTemplateDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/onboarding/templates/{id}/clone
    /// Clones an existing template incl. all its tasks under a new name (FR-6). Body (optional): { newName }.
    /// </summary>
    [HttpPost("{id:guid}/clone")]
    [RequirePermission("Onboarding.Manage")]
    [ProducesResponseType(typeof(ApiResponse<OnboardingTemplateDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Clone(
        Guid id, [FromBody] CloneOnboardingTemplateRequest? request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CloneChecklistTemplateCommand(id, request?.NewName), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Value!.Id },
            ApiResponse<OnboardingTemplateDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/onboarding/templates/{id}/activate
    /// Activates a template (FR-7).
    /// </summary>
    [HttpPost("{id:guid}/activate")]
    [RequirePermission("Onboarding.Manage")]
    [ProducesResponseType(typeof(ApiResponse<OnboardingTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetTemplateActiveCommand(id, true), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<OnboardingTemplateDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/onboarding/templates/{id}/deactivate
    /// Deactivates a template without deletion (FR-7, BR-4). It stays visible but cannot be assigned.
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission("Onboarding.Manage")]
    [ProducesResponseType(typeof(ApiResponse<OnboardingTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetTemplateActiveCommand(id, false), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<OnboardingTemplateDto>.Ok(result.Value!));
    }
}
