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
/// Recruiter-facing offer-letter endpoints (US-REC-007). All operations are authenticated and tenant-scoped
/// via the EF global query filter (AC-5). Writes (generate / send / respond / withdraw) require
/// <c>Recruitment.Manage</c> (BR-1 — the story's Recruitment.Offer.All permission does not exist in the
/// catalog; Recruitment.Manage is the closest existing recruiter-write permission); reads + the document
/// download require <c>Recruitment.View</c>.
/// </summary>
[ApiController]
[Route("api/v1/recruitment")]
[Authorize]
public sealed class OffersController : ControllerBase
{
    private readonly IMediator _mediator;

    public OffersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// POST /api/v1/recruitment/offers
    /// Generates an offer letter for an applicant (AC-1/FR-1/FR-2/FR-9). Requires Recruitment.Manage.
    /// </summary>
    [HttpPost("offers")]
    [RequirePermission("Recruitment.Manage")]
    [ProducesResponseType(typeof(ApiResponse<OfferDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Generate(
        [FromBody] GenerateOfferRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GenerateOfferCommand(
            request.ApplicantId, request.OfferedPosition, request.DepartmentId,
            request.ReportingManagerEmployeeId, request.SalaryAmount, request.Currency,
            request.SalaryFrequency, request.BenefitsSummary, request.StartDate, request.ExpiryDate,
            request.ProbationMonths, request.CustomClauses, request.SalaryStructureId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<OfferDto>.Ok(result.Value!, "Offer generated."));
    }

    /// <summary>
    /// POST /api/v1/recruitment/offers/{offerId}/send
    /// Sends a Draft offer to the applicant (AC-2/FR-5/FR-7). Requires Recruitment.Manage.
    /// </summary>
    [HttpPost("offers/{offerId:guid}/send")]
    [RequirePermission("Recruitment.Manage")]
    [ProducesResponseType(typeof(ApiResponse<OfferDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Send(Guid offerId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SendOfferCommand(offerId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<OfferDto>.Ok(result.Value!, "Offer sent."));
    }

    /// <summary>
    /// POST /api/v1/recruitment/offers/{offerId}/respond
    /// Records the applicant's response (AC-3/BR-3): accepted advances to Hired, declined stays in Offer.
    /// Requires Recruitment.Manage.
    /// </summary>
    [HttpPost("offers/{offerId:guid}/respond")]
    [RequirePermission("Recruitment.Manage")]
    [ProducesResponseType(typeof(ApiResponse<OfferDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Respond(
        Guid offerId,
        [FromBody] RespondToOfferRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RespondToOfferCommand(offerId, request.Accepted), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<OfferDto>.Ok(result.Value!, "Offer response recorded."));
    }

    /// <summary>
    /// POST /api/v1/recruitment/offers/{offerId}/withdraw
    /// Withdraws an offer before acceptance (FR-8/BR-7). Requires Recruitment.Manage.
    /// </summary>
    [HttpPost("offers/{offerId:guid}/withdraw")]
    [RequirePermission("Recruitment.Manage")]
    [ProducesResponseType(typeof(ApiResponse<OfferDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Withdraw(Guid offerId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new WithdrawOfferCommand(offerId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<OfferDto>.Ok(result.Value!, "Offer withdrawn."));
    }

    /// <summary>
    /// GET /api/v1/recruitment/offers/{offerId}
    /// Gets a single offer (tenant-scoped). Requires Recruitment.View.
    /// </summary>
    [HttpGet("offers/{offerId:guid}")]
    [RequirePermission("Recruitment.View")]
    [ProducesResponseType(typeof(ApiResponse<OfferDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid offerId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOfferByIdQuery(offerId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<OfferDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/recruitment/applicants/{applicantId}/offers
    /// Lists all offers for an applicant, newest version first (FR-9). Requires Recruitment.View.
    /// </summary>
    [HttpGet("applicants/{applicantId:guid}/offers")]
    [RequirePermission("Recruitment.View")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<OfferDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListByApplicant(Guid applicantId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListOffersForApplicantQuery(applicantId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<OfferDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/recruitment/offers/{offerId}/document
    /// Streams the generated offer-letter PDF through the API (AC-1/FR-4 preview + download). Requires
    /// Recruitment.View.
    /// </summary>
    [HttpGet("offers/{offerId:guid}/document")]
    [RequirePermission("Recruitment.View")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadDocument(Guid offerId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DownloadOfferPdfQuery(offerId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var doc = result.Value!;
        return File(doc.Content, doc.ContentType, doc.FileName);
    }
}

/// <summary>
/// Request body to generate an offer (US-REC-007 FR-1). camelCase on the wire. The reference number,
/// version, PDF, and status are server-managed, so they are not part of the request. expiryDate is
/// optional — the server defaults it to generation date + 7 days (BR-6) when omitted.
/// </summary>
public sealed record GenerateOfferRequest
{
    public Guid ApplicantId { get; init; }
    public string OfferedPosition { get; init; } = string.Empty;
    public Guid? DepartmentId { get; init; }
    public Guid? ReportingManagerEmployeeId { get; init; }
    public decimal SalaryAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public SalaryFrequency SalaryFrequency { get; init; } = SalaryFrequency.Monthly;

    /// <summary>
    /// Decision D1 (BUG-292): optional salary structure the compensation is agreed against at offer time.
    /// Must belong to the calling tenant (rejected otherwise). Carried through to salary assignment when the
    /// applicant is converted to an employee, so the new hire is payroll-ready.
    /// </summary>
    public Guid? SalaryStructureId { get; init; }

    public string? BenefitsSummary { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public int? ProbationMonths { get; init; }
    public string? CustomClauses { get; init; }
}

/// <summary>Request body to record an offer response (US-REC-007 AC-3). camelCase on the wire.</summary>
public sealed record RespondToOfferRequest
{
    /// <summary>True = accepted (advance to Hired, BR-3); false = declined (stays in Offer).</summary>
    public bool Accepted { get; init; }
}
