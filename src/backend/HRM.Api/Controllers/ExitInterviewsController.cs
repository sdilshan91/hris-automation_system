using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using HRM.Application.Features.Onboarding.Commands;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Application.Features.Onboarding.Queries;
using HRM.Domain.Authorization;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped exit-interview endpoints (US-ONB-006). HR-conducted recording + analytics require the HR
/// <c>Onboarding.Manage</c> permission; self-service recording is allowed for the authenticated departing
/// employee on their OWN offboarding (the service scopes it). Individual detail (incl. free-text PII) and
/// de-anonymized access require <c>ExitInterview.ViewDetail</c> (FR-5/NFR-6). All operations are tenant-scoped
/// via the EF global query filter; the tenant_id is taken from the resolved session, never user input (FR-6).
/// Base path: api/v1/exit-interviews.
/// </summary>
[ApiController]
[Route("api/v1/exit-interviews")]
[Authorize]
public sealed class ExitInterviewsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;

    public ExitInterviewsController(IMediator mediator, ICurrentUser currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>GET /api/v1/exit-interviews/template — FR-1/AC-1: the tenant's questionnaire template (seeded DEFAULT if none).</summary>
    [HttpGet("template")]
    [ProducesResponseType(typeof(ApiResponse<ExitInterviewTemplateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTemplate(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetExitInterviewTemplateQuery(), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<ExitInterviewTemplateDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/exit-interviews?offboardingId={id} — FR-3: the exit interview for an offboarding (or 404).
    /// Free-text PII answers/comments are included only for callers with ExitInterview.ViewDetail (FR-5/NFR-6).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ExitInterviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByOffboarding(
        [FromQuery] Guid offboardingId, CancellationToken cancellationToken)
    {
        var includeFreeText = HasDetailPermission();
        var result = await _mediator.Send(
            new GetExitInterviewByOffboardingQuery(offboardingId, includeFreeText), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<ExitInterviewDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/exit-interviews — AC-2/FR-3/FR-8: record an exit interview. HR-conducted requires
    /// Onboarding.Manage; self-service is allowed for the authenticated departing employee on their own
    /// offboarding (BR-3). Persists responses, links to the offboarding, completes the exit-interview task, and
    /// (self-service) writes an HR-notification outbox row. BR-1: a duplicate is rejected (409) unless the
    /// caller holds ExitInterview.ViewDetail (re-version, BR-2).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ExitInterviewDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Record(
        [FromBody] RecordExitInterviewInput input, CancellationToken cancellationToken)
    {
        var isSelfService = string.Equals(
            input.InterviewMode?.Trim(), "self_service", StringComparison.OrdinalIgnoreCase);

        // HR-conducted recording requires the HR manage permission; self-service is open to the departing
        // employee (the service scopes it to their own offboarding).
        if (!isSelfService && !_currentUser.Permissions.Contains(PermissionCatalog.Onboarding.Manage))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail(
                "You do not have permission to record an HR-conducted exit interview.", "forbidden"));

        var command = new RecordExitInterviewCommand(input, isSelfService, AllowEdit: HasDetailPermission());

        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return CreatedAtAction(
            nameof(GetByOffboarding),
            new { offboardingId = result.Value!.OffboardingInstanceId },
            ApiResponse<ExitInterviewDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/exit-interviews/analytics?fromDate=&amp;toDate= — FR-4/FR-5: tenant-scoped AGGREGATED analytics
    /// (anonymized). Requires the HR Onboarding.Manage permission. De-anonymized access is gated by
    /// ExitInterview.ViewDetail and flagged in the audit log (NFR-6).
    /// </summary>
    [HttpGet("analytics")]
    [RequirePermission("Onboarding.Manage")]
    [ProducesResponseType(typeof(ApiResponse<ExitInterviewAnalyticsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAnalytics(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetExitInterviewAnalyticsQuery(fromDate, toDate, HasDetailPermission()), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<ExitInterviewAnalyticsDto>.Ok(result.Value!));
    }

    private bool HasDetailPermission()
        => _currentUser.Permissions.Contains(PermissionCatalog.ExitInterview.ViewDetail);
}
