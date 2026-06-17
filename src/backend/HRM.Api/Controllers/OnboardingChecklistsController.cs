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
/// Tenant-scoped HR endpoints for assigning onboarding checklists to new hires (US-ONB-002). All
/// operations require <c>Onboarding.Manage</c> (the same authorization the templates controller uses for
/// writes) and are tenant-scoped via the EF global query filter; the tenant_id is always taken from the
/// resolved session context, never user input (FR-7). Notification dispatch follows the outbox pattern
/// (NFR-3): assignment writes intent rows in the same transaction and a Hangfire worker delivers them.
/// </summary>
[ApiController]
[Route("api/v1/onboarding/checklists")]
[Authorize]
public sealed class OnboardingChecklistsController : ControllerBase
{
    private readonly IMediator _mediator;

    public OnboardingChecklistsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/onboarding/checklists/applicable-templates?employeeId={id}
    /// AC-1/FR-1: lists active templates applicable to an employee (dept + job title + universal).
    /// </summary>
    [HttpGet("applicable-templates")]
    [RequirePermission("Onboarding.Manage")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ApplicableTemplateDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApplicableTemplates(
        [FromQuery] Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetApplicableTemplatesQuery(employeeId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<ApplicableTemplateDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/onboarding/checklists/{id}
    /// Gets a single assigned checklist instance (with its task instances) by id, tenant-scoped.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Onboarding.Manage")]
    [ProducesResponseType(typeof(ApiResponse<OnboardingChecklistInstanceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetChecklistInstanceQuery(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<OnboardingChecklistInstanceDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/onboarding/checklists
    /// AC-2/AC-3: assigns a checklist to a new hire. Creates task instances with calculated due dates and
    /// pending status, resolves responsible parties (FR-3), and queues notifications via the outbox (NFR-3).
    /// The Idempotency-Key header (or body) makes a retried assignment within the session idempotent (NFR-5).
    /// </summary>
    [HttpPost]
    [RequirePermission("Onboarding.Manage")]
    [ProducesResponseType(typeof(ApiResponse<OnboardingChecklistInstanceDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Assign(
        [FromBody] AssignChecklistRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var command = new AssignChecklistCommand(
            request.EmployeeId,
            request.TemplateId,
            request.OverrideStartDate,
            request.Mode,
            request.AdditionalTasks.Select(t => new AssignChecklistAdHocTask(
                t.Title, t.Description, t.Category, t.ResponsibleRole, t.ResponsibleUserId,
                t.DueOffsetDays, t.IsMandatory, t.SortOrder)).ToList(),
            // The header takes precedence over the body key (NFR-5).
            string.IsNullOrWhiteSpace(idempotencyKey) ? request.IdempotencyKey : idempotencyKey);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Value!.Id },
            ApiResponse<OnboardingChecklistInstanceDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/onboarding/checklists/{id}
    /// AC-4/FR-5/FR-6: modifies an assigned checklist — add ad-hoc tasks, change due dates, soft-delete
    /// non-mandatory tasks. Mandatory tasks cannot be removed (BR-3).
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("Onboarding.Manage")]
    [ProducesResponseType(typeof(ApiResponse<OnboardingChecklistInstanceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Modify(
        Guid id, [FromBody] ModifyChecklistRequest request, CancellationToken cancellationToken)
    {
        var command = new ModifyAssignedChecklistCommand(
            id,
            request.AddTasks.Select(t => new AssignChecklistAdHocTask(
                t.Title, t.Description, t.Category, t.ResponsibleRole, t.ResponsibleUserId,
                t.DueOffsetDays, t.IsMandatory, t.SortOrder)).ToList(),
            request.TaskChanges.Select(c => new ModifyChecklistTaskChange(
                c.TaskInstanceId, c.NewDueDate, c.Remove)).ToList());

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<OnboardingChecklistInstanceDto>.Ok(result.Value!));
    }
}
