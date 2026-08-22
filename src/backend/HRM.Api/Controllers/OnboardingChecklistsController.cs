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
    /// GET /api/v1/onboarding/checklists/tasks/{taskInstanceId}/attachment
    /// GAP-027: streams a task's attachment. my-checklist rendered the old value as an anchor href, and it
    /// pointed at `/files/{tenantId}/{path}` — a scheme no route serves — so the link opened a 404.
    /// </summary>
    [HttpGet("tasks/{taskInstanceId:guid}/attachment")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadTaskAttachment(
        Guid taskInstanceId, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new DownloadTaskAttachmentQuery(taskInstanceId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!));

        var file = result.Value!;
        return File(file.Content, file.ContentType, file.FileName);
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
    /// GET /api/v1/onboarding/checklists/employee/{employeeId}
    /// GAP-013 / AC-3: the employee's current ACTIVE checklist, or 200 with a null body when they have none.
    ///
    /// <para>Route ordering matters: this is declared AFTER <c>{id:guid}</c> in the file but the literal
    /// <c>employee/</c> segment makes it unambiguous — <c>{id:guid}</c> cannot match a two-segment path.</para>
    ///
    /// <para>200-with-null rather than 404: having no checklist is the normal state before a first
    /// assignment, and the assignment screen needs to distinguish "none yet" (assign freely) from
    /// "already has one" (show the AC-3 replace/merge prompt). A 404 would collapse those two.</para>
    /// </summary>
    [HttpGet("employee/{employeeId:guid}")]
    [RequirePermission("Onboarding.Manage")]
    [ProducesResponseType(typeof(ApiResponse<OnboardingChecklistInstanceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveByEmployee(Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetActiveChecklistByEmployeeQuery(employeeId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<OnboardingChecklistInstanceDto?>.Ok(result.Value));
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

    // ── US-ONB-003: self-service (the authenticated employee acts on their OWN tasks) ──
    // These endpoints intentionally require ONLY authentication, NOT Onboarding.Manage — the new hire is a
    // regular employee acting on their own checklist (FR-7 ownership is enforced in the service, not by a
    // management permission).

    /// <summary>
    /// GET /api/v1/onboarding/checklists/me
    /// US-ONB-003 AC-1/AC-2/FR-1: the logged-in employee's active checklist with tasks grouped by category
    /// and a progress summary. The employee is resolved from the JWT; tasks are tenant-scoped.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<MyChecklistDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyChecklist(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyChecklistQuery(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<MyChecklistDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/onboarding/checklists/me/progress
    /// US-ONB-003 AC-1/FR-4: the cheap progress summary for the dashboard widget (percent + counts).
    /// </summary>
    [HttpGet("me/progress")]
    [ProducesResponseType(typeof(ApiResponse<MyChecklistProgressDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyChecklistProgress(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyChecklistProgressQuery(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<MyChecklistProgressDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/onboarding/checklists/tasks/{taskInstanceId}/complete
    /// US-ONB-003 AC-3/AC-4/FR-2/FR-3: the employee marks their own task complete, optionally with a comment
    /// and a document (multipart, form field "attachment"). Enforces ownership (FR-7/BR-1), cannot-revert
    /// (BR-3), document-required (AC-4), and MIME + size (FR-3/NFR-6) in the service. Accepts either a JSON
    /// body { comment } or multipart/form-data with "comment" + "attachment".
    /// </summary>
    [HttpPost("tasks/{taskInstanceId:guid}/complete")]
    [ProducesResponseType(typeof(ApiResponse<CompleteTaskResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [RequestSizeLimit(11 * 1024 * 1024)] // 10 MB + multipart overhead (NFR-6).
    public async Task<IActionResult> CompleteTask(
        Guid taskInstanceId,
        [FromForm] string? comment,
        IFormFile? attachment,
        CancellationToken cancellationToken)
    {
        if (attachment is not null && attachment.Length == 0)
            return BadRequest(ApiResponse.Fail("Uploaded file is empty."));

        Stream? stream = attachment is not null ? attachment.OpenReadStream() : null;
        try
        {
            var command = new CompleteTaskCommand(
                taskInstanceId,
                comment,
                stream,
                attachment?.FileName,
                attachment?.ContentType,
                attachment?.Length ?? 0);

            var result = await _mediator.Send(command, cancellationToken);

            if (result.IsFailure)
                return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

            return Ok(ApiResponse<CompleteTaskResultDto>.Ok(result.Value!));
        }
        finally
        {
            if (stream is not null)
                await stream.DisposeAsync();
        }
    }
}
