using HRM.Application.DTOs;
using HRM.Application.Features.NotificationTemplates.Commands;
using HRM.Application.Features.NotificationTemplates.DTOs;
using HRM.Application.Features.NotificationTemplates.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// US-NTF-002: Tenant-Admin email notification template management (Settings &gt; Notifications &amp; Email &gt;
/// Templates). List events with override status (AC-1), edit/save an override (AC-2/AC-3), reset to default (AC-4),
/// live preview (FR-4) and a test-email send (FR-8). All operations target only the current tenant via
/// ITenantContext (AC-5); reads require <c>Tenant.ViewSettings</c>, writes require <c>Tenant.ManageSettings</c>
/// (held by Tenant Admin / Tenant Owner — same gate as the company-settings console). Thin: dispatches via MediatR
/// and wraps in <c>ApiResponse&lt;T&gt;</c>.
/// </summary>
[ApiController]
[Route("api/v1/notification-templates")]
[Authorize]
public sealed class NotificationTemplatesController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotificationTemplatesController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/notification-templates?language=en — one row per catalog event with override status (AC-1).</summary>
    [HttpGet]
    [RequirePermission("Tenant.ViewSettings")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TemplateListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? language = null, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new ListTemplatesQuery(language), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<TemplateListItemDto>>.Ok(result.Value!));
    }

    /// <summary>GET /api/v1/notification-templates/{eventKey}?language=en — the effective template + placeholders (AC-2).</summary>
    [HttpGet("{eventKey}")]
    [RequirePermission("Tenant.ViewSettings")]
    [ProducesResponseType(typeof(ApiResponse<TemplateDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        string eventKey, [FromQuery] string? language = null, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetTemplateQuery(eventKey, language), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<TemplateDetailDto>.Ok(result.Value!));
    }

    /// <summary>PUT /api/v1/notification-templates/{eventKey}?language=en — upsert the tenant override (AC-3).</summary>
    [HttpPut("{eventKey}")]
    [RequirePermission("Tenant.ManageSettings")]
    [ProducesResponseType(typeof(ApiResponse<TemplateDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Save(
        string eventKey,
        [FromBody] TemplateBodyRequest body,
        [FromQuery] string? language = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new SaveTemplateCommand(eventKey, language, body), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<TemplateDetailDto>.Ok(result.Value!, "Template saved."));
    }

    /// <summary>DELETE /api/v1/notification-templates/{eventKey}?language=en — reset to default; returns the default (AC-4).</summary>
    [HttpDelete("{eventKey}")]
    [RequirePermission("Tenant.ManageSettings")]
    [ProducesResponseType(typeof(ApiResponse<TemplateDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reset(
        string eventKey, [FromQuery] string? language = null, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new ResetTemplateCommand(eventKey, language), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<TemplateDetailDto>.Ok(result.Value!, "Template reset to default."));
    }

    /// <summary>POST /api/v1/notification-templates/{eventKey}/preview — render the supplied bodies with sample data (FR-4).</summary>
    [HttpPost("{eventKey}/preview")]
    [RequirePermission("Tenant.ViewSettings")]
    [ProducesResponseType(typeof(ApiResponse<RenderedPreviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview(
        string eventKey, [FromBody] PreviewTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new PreviewTemplateCommand(eventKey, request), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<RenderedPreviewDto>.Ok(result.Value!));
    }

    /// <summary>POST /api/v1/notification-templates/{eventKey}/test-email — send a rendered test email (FR-8).</summary>
    [HttpPost("{eventKey}/test-email")]
    [RequirePermission("Tenant.ManageSettings")]
    [ProducesResponseType(typeof(ApiResponse<TestEmailResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendTestEmail(
        string eventKey, [FromBody] TestEmailRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new SendTestEmailCommand(eventKey, request), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<TestEmailResultDto>.Ok(result.Value!));
    }
}
