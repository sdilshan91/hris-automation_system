using HRM.Application.DTOs;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Application.Features.Recruitment.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Anonymous candidate-portal endpoints (US-REC-008). NO JWT: access is authenticated by a magic-link token
/// (BR-6), and the tenant is resolved from the request subdomain by TenantResolutionMiddleware (which runs
/// before auth), exactly like <see cref="CareersController"/>. The token is bound to a tenant + email + expiry
/// (HMAC-SHA256, NFR-4); the portal only ever serves data for the subdomain-resolved tenant that also matches
/// the token's tenant (AC-4/BR-4), and the EF global query filter is the second isolation layer.
///
/// The magic-link token is passed in the <c>X-Portal-Token</c> header (kept out of the URL/query so it does
/// not leak into logs/referrers); the regenerate endpoint takes the email in the body instead.
/// </summary>
[ApiController]
[Route("api/v1/careers/portal")]
[AllowAnonymous]
public sealed class PortalController : ControllerBase
{
    private const string PortalTokenHeader = "X-Portal-Token";

    private readonly IMediator _mediator;

    public PortalController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/careers/portal/dashboard
    /// Returns the sanitized candidate dashboard for the email behind the presented token (FR-2/AC-1/AC-2/
    /// AC-3/FR-6). 401 if the token is missing/invalid/expired/cross-tenant.
    /// </summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(ApiResponse<PortalDashboardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetDashboard(CancellationToken cancellationToken)
    {
        if (!TryGetToken(out var token))
            return Unauthorized(ApiResponse.Fail("A portal link token is required.", "token_required"));

        var result = await _mediator.Send(new GetPortalDashboardQuery(token), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 401, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<PortalDashboardDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/careers/portal/request-link  (body: { email })
    /// A candidate whose link expired requests a fresh one (BR-5/FR-8). Always returns a generic success
    /// (anti-enumeration) — a link is only actually issued if an application exists for that email.
    /// </summary>
    [HttpPost("request-link")]
    [ProducesResponseType(typeof(ApiResponse<PortalLinkRequestResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestLink(
        [FromBody] RequestPortalLinkRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RequestPortalLinkCommand(request.Email), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<PortalLinkRequestResultDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/careers/portal/offers/{offerId}/respond  (body: { accept })
    /// The candidate accepts/declines an offer via the portal (FR-5/AC-3/BR-2). Accept advances them to Hired
    /// (BR-3). 401 invalid token; 403 not the token-holder's offer; 404/409 per offer lifecycle.
    /// </summary>
    [HttpPost("offers/{offerId:guid}/respond")]
    [ProducesResponseType(typeof(ApiResponse<PortalOfferDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RespondToOffer(
        Guid offerId,
        [FromBody] PortalOfferResponseRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetToken(out var token))
            return Unauthorized(ApiResponse.Fail("A portal link token is required.", "token_required"));

        var result = await _mediator.Send(
            new RespondToPortalOfferCommand(token, offerId, request.Accept), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<PortalOfferDto>.Ok(result.Value!, "Your response has been recorded."));
    }

    /// <summary>
    /// GET /api/v1/careers/portal/applicants/{applicantId}/resume
    /// Streams the candidate's own resume (FR-4). 401/403/404 as above.
    /// </summary>
    [HttpGet("applicants/{applicantId:guid}/resume")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadResume(Guid applicantId, CancellationToken cancellationToken)
    {
        if (!TryGetToken(out var token))
            return Unauthorized(ApiResponse.Fail("A portal link token is required.", "token_required"));

        var result = await _mediator.Send(new DownloadPortalResumeQuery(token, applicantId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var doc = result.Value!;
        return File(doc.Content, doc.ContentType, doc.FileName);
    }

    /// <summary>
    /// GET /api/v1/careers/portal/offers/{offerId}/document
    /// Streams the offer-letter PDF for the candidate's own offer (FR-4/AC-3). 401/403/404 as above.
    /// </summary>
    [HttpGet("offers/{offerId:guid}/document")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadOfferDocument(Guid offerId, CancellationToken cancellationToken)
    {
        if (!TryGetToken(out var token))
            return Unauthorized(ApiResponse.Fail("A portal link token is required.", "token_required"));

        var result = await _mediator.Send(new DownloadPortalOfferDocumentQuery(token, offerId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var doc = result.Value!;
        return File(doc.Content, doc.ContentType, doc.FileName);
    }

    /// <summary>Reads the magic-link token from the <c>X-Portal-Token</c> header.</summary>
    private bool TryGetToken(out string token)
    {
        token = Request.Headers[PortalTokenHeader].ToString();
        return !string.IsNullOrWhiteSpace(token);
    }
}

/// <summary>Request body to request a fresh portal link (US-REC-008 BR-5). camelCase on the wire.</summary>
public sealed record RequestPortalLinkRequest
{
    public string Email { get; init; } = string.Empty;
}

/// <summary>Request body for a portal offer accept/decline (US-REC-008 FR-5). camelCase on the wire.</summary>
public sealed record PortalOfferResponseRequest
{
    /// <summary>True = accept (advance to Hired, BR-3); false = decline.</summary>
    public bool Accept { get; init; }
}
