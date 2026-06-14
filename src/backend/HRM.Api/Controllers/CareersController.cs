using HRM.Application.DTOs;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Application.Features.Recruitment.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Anonymous public careers-page endpoints (US-REC-001 FR-4/FR-5/NFR-5). No authentication: the tenant
/// is resolved from the request subdomain by TenantResolutionMiddleware (which runs before auth), and
/// the EF global query filter scopes every read to that tenant. Only Open + public-enabled vacancies of
/// a tenant whose PublicCareersEnabled toggle is on are returned (BR-5) — never a Draft or internal one.
/// </summary>
[ApiController]
[Route("api/v1/careers/vacancies")]
[AllowAnonymous]
public sealed class CareersController : ControllerBase
{
    private readonly IMediator _mediator;

    public CareersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/careers/vacancies
    /// Lists the Open, publicly-visible vacancies for the tenant resolved by subdomain (FR-4). Returns an
    /// empty list when the tenant's public careers toggle is off.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PublicVacancyListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListOpen(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListPublicVacanciesQuery(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<PublicVacancyListItemDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/careers/vacancies/{slug}
    /// Gets a single Open, publicly-visible vacancy by its SEO slug (FR-5). 404 when not found, not Open,
    /// not public, or the tenant toggle is off.
    /// </summary>
    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(ApiResponse<PublicVacancyDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPublicVacancyBySlugQuery(slug), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<PublicVacancyDetailDto>.Ok(result.Value!));
    }
}
