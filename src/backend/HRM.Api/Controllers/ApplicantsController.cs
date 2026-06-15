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
/// Authenticated, tenant-scoped recruitment endpoints for applicants (US-REC-002):
/// the internal application submit path (an authenticated employee applies — AC-4/FR-8) and the
/// recruiter-facing list/detail of applicants for a vacancy. Reads require <c>Recruitment.View</c>;
/// the recruiter detail also uses <c>Recruitment.View</c>. The internal submit only requires an
/// authenticated user (any logged-in employee may apply). All operations are tenant-scoped via the EF
/// global query filter (AC-5). The anonymous public submit lives in <see cref="CareersController"/>.
/// </summary>
[ApiController]
[Route("api/v1/recruitment/vacancies/{vacancyId:guid}/applicants")]
[Authorize]
public sealed class ApplicantsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ApplicantsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/recruitment/vacancies/{vacancyId}/applicants
    /// Lists applicants for a vacancy (paged). Recruiter-facing — requires Recruitment.View.
    /// Returns the contract shape { data, total, page, pageSize }.
    /// </summary>
    [HttpGet]
    [RequirePermission("Recruitment.View")]
    [ProducesResponseType(typeof(ApiResponse<ApplicantPageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        Guid vacancyId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new ListApplicantsByVacancyQuery(vacancyId, page, pageSize), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var paged = result.Value!;
        var response = new ApplicantPageResponse
        {
            Data = paged.Items,
            Total = paged.TotalCount,
            Page = paged.Page,
            PageSize = paged.PageSize,
        };

        return Ok(ApiResponse<ApplicantPageResponse>.Ok(response));
    }

    /// <summary>
    /// GET /api/v1/recruitment/vacancies/{vacancyId}/applicants/{id}
    /// Gets a single applicant (tenant-scoped). Recruiter-facing — requires Recruitment.View.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Recruitment.View")]
    [ProducesResponseType(typeof(ApiResponse<ApplicantDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid vacancyId, Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetApplicantByIdQuery(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<ApplicantDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/recruitment/vacancies/{vacancyId}/applicants  (multipart/form-data)
    /// Internal application submission by an authenticated employee (US-REC-002 AC-4/FR-8/BR-5). The
    /// record is flagged internal and linked to the employee record. Same vacancy/MIME/size/duplicate
    /// rules as the public path. Pre-fill of the form from the employee profile is a frontend concern.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ApplicationConfirmationDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitInternal(
        Guid vacancyId,
        IFormFile resume,
        [FromForm] SubmitInternalApplicationRequest form,
        CancellationToken cancellationToken)
    {
        if (resume is null || resume.Length == 0)
            return BadRequest(ApiResponse.Fail("A resume file is required.", "resume_required"));

        await using var stream = resume.OpenReadStream();
        var command = new SubmitApplicationCommand(
            vacancyId, form.FirstName, form.LastName, form.Email, form.Phone, form.CoverLetter,
            stream, resume.FileName, resume.ContentType, resume.Length,
            ApplicationSource.Internal, form.LinkedEmployeeId);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<ApplicationConfirmationDto>.Ok(result.Value!, "Application submitted successfully."));
    }
}

/// <summary>
/// Frontend list-response contract: { data, total, page, pageSize }. Mirrors VacancyPageResponse so the
/// recruitment UI consumes a consistent shape.
/// </summary>
public sealed record ApplicantPageResponse
{
    public IReadOnlyList<ApplicantListItemDto> Data { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}
