using HRM.Application.DTOs;
using HRM.Application.Features.CustomFields.Commands;
using HRM.Application.Features.CustomFields.DTOs;
using HRM.Application.Features.CustomFields.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped custom field definition management endpoints (US-CHR-012).
/// Requires CustomField.* permissions (Tenant Admin only).
/// </summary>
[ApiController]
[Route("api/v1/tenant/custom-fields")]
[Authorize]
public sealed class CustomFieldsController : ControllerBase
{
    private readonly IMediator _mediator;

    public CustomFieldsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/tenant/custom-fields
    /// Lists all custom field definitions grouped by entity type, with usage count (AC-1).
    /// </summary>
    [HttpGet]
    [RequirePermission("CustomField.View")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CustomFieldDefinitionListResult>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCustomFields(
        [FromQuery] string? entityType = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetCustomFieldsQuery(entityType), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<IReadOnlyList<CustomFieldDefinitionListResult>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/custom-fields/{id}
    /// Gets a single custom field definition by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("CustomField.View")]
    [ProducesResponseType(typeof(ApiResponse<CustomFieldDefinitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCustomFieldById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetCustomFieldByIdQuery(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<CustomFieldDefinitionDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/custom-fields
    /// Creates a new custom field definition (AC-2).
    /// </summary>
    [HttpPost]
    [RequirePermission("CustomField.Create")]
    [ProducesResponseType(typeof(ApiResponse<CustomFieldDefinitionDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateCustomField(
        [FromBody] CreateCustomFieldRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateCustomFieldCommand(
            request.EntityType, request.FieldName, request.FieldKey,
            request.FieldType, request.IsRequired, request.Options,
            request.DisplayOrder);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return CreatedAtAction(
            nameof(GetCustomFieldById),
            new { id = result.Value!.Id },
            ApiResponse<CustomFieldDefinitionDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/tenant/custom-fields/{id}
    /// Updates an existing custom field definition. Field type and key are immutable.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("CustomField.Edit")]
    [ProducesResponseType(typeof(ApiResponse<CustomFieldDefinitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCustomField(
        Guid id, [FromBody] UpdateCustomFieldRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateCustomFieldCommand(
            id, request.FieldName, request.IsRequired, request.Options, request.DisplayOrder);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<CustomFieldDefinitionDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/custom-fields/{id}/deactivate
    /// Deactivates a custom field (hides from forms, preserves JSONB data) (FR-7, AC-5).
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission("CustomField.Deactivate")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateCustomField(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeactivateCustomFieldCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse.Ok("Custom field deactivated successfully."));
    }

    /// <summary>
    /// POST /api/v1/tenant/custom-fields/{id}/reactivate
    /// Reactivates a previously deactivated custom field (AC-5).
    /// </summary>
    [HttpPost("{id:guid}/reactivate")]
    [RequirePermission("CustomField.Edit")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReactivateCustomField(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ReactivateCustomFieldCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse.Ok("Custom field reactivated successfully."));
    }

    /// <summary>
    /// POST /api/v1/tenant/custom-fields/reorder
    /// Reorders custom fields by display_order (FR-8).
    /// </summary>
    [HttpPost("reorder")]
    [RequirePermission("CustomField.Edit")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReorderCustomFields(
        [FromBody] ReorderCustomFieldsRequest request, CancellationToken cancellationToken)
    {
        var entityType = "employee"; // Phase 1: only employee entity type
        var command = new ReorderCustomFieldsCommand(entityType, request.FieldIds);
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse.Ok("Custom fields reordered successfully."));
    }
}
