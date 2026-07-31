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
/// Recruiter/HR-facing endpoints to convert an accepted applicant into a Core HR employee (US-REC-010).
/// All operations are authenticated and tenant-scoped via the EF global query filter (AC-5). Both the
/// pre-fill read and the convert write require BOTH <c>Recruitment.Manage</c> AND <c>Employee.Create</c>
/// (BR-1 — the story names Recruitment.Manage.All + Employee.Create.All, which are not concrete catalog
/// entries; the closest existing permissions are Recruitment.Manage + Employee.Create, ANDed via two
/// RequirePermission attributes). This excludes the plain Recruiter role — which holds Recruitment.Manage
/// but NOT Employee.Create — so only HR Officer / HR Manager / Tenant Admin / Owner can convert, matching
/// the story persona ("HR Officer").
/// </summary>
[ApiController]
[Route("api/v1/recruitment")]
[Authorize]
[RequirePermission("Recruitment.Manage")]
[RequirePermission("Employee.Create")]
public sealed class ApplicantConversionController : ControllerBase
{
    private readonly IMediator _mediator;

    public ApplicantConversionController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/recruitment/applicants/{applicantId}/conversion-prefill
    /// Returns the pre-filled employee form data (AC-1/FR-2): applicant + accepted-offer mapping. 409 if the
    /// applicant is not Hired / has no accepted offer.
    /// </summary>
    [HttpGet("applicants/{applicantId:guid}/conversion-prefill")]
    [ProducesResponseType(typeof(ApiResponse<ConversionPrefillDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetPrefill(Guid applicantId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetConversionPrefillQuery(applicantId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<ConversionPrefillDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/recruitment/applicants/{applicantId}/convert
    /// Converts the applicant to an employee (AC-2/AC-4). Atomic (NFR-3). Returns the new employee id +
    /// number and the updated vacancy filled/headcount ratio. 409 if already converted (FR-10/BR-2) or the
    /// applicant is not eligible (FR-1); 403 if the subscription plan limit is exceeded (BR-3).
    /// </summary>
    [HttpPost("applicants/{applicantId:guid}/convert")]
    [ProducesResponseType(typeof(ApiResponse<ConversionResultDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Convert(
        Guid applicantId,
        [FromBody] ConvertApplicantRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ConvertApplicantToEmployeeCommand(
            applicantId, request.JobTitleId, request.DepartmentId, request.EmploymentType,
            request.DateOfJoining, request.ReportsToEmployeeId, request.LocationId, request.EmployeeNo,
            request.DateOfBirth, request.Gender), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<ConversionResultDto>.Ok(result.Value!, "Applicant converted to employee."));
    }
}

/// <summary>
/// Request body to convert an applicant to an employee (US-REC-010). camelCase on the wire. The applicant
/// id comes from the route. Name/email/phone are mapped server-side from the application + accepted offer;
/// this body carries the fields the HR Officer reviews/completes on the form (FR-3).
///
/// <para><b>SALARY IS NOT CARRIED HERE, AND IS NOT MAPPED SERVER-SIDE (BUG-292).</b> This summary previously
/// claimed salary was "mapped server-side from the application + accepted offer". It never was: there is no
/// Salary property on this record, no <c>EmployeeSalaryComponent</c> is written, and no
/// <c>SalaryStructure</c> is assigned. The frontend was sending <c>salaryAmount</c>/<c>currency</c>, model
/// binding silently dropped both, and the conversion still reported success — so HR could enter a salary,
/// see it echoed back, and lose it. That false claim is very likely why the gap survived review.</para>
///
/// <para>Assigning a real salary needs a <c>SalaryStructureId</c> (see <c>AssignSalaryStructureCommand</c>),
/// which an offer's flat <c>SalaryAmount</c> cannot supply — mapping an amount onto a structure is a product
/// decision, not a wiring job. Until that is settled the salary/currency fields are shown READ-ONLY on the
/// conversion form and the employee's salary is assigned as a separate, deliberate step.</para>
/// </summary>
public sealed record ConvertApplicantRequest
{
    public Guid JobTitleId { get; init; }
    public Guid DepartmentId { get; init; }
    public EmploymentType EmploymentType { get; init; } = EmploymentType.FullTime;
    public DateTime DateOfJoining { get; init; }
    public Guid? ReportsToEmployeeId { get; init; }
    public Guid? LocationId { get; init; }

    /// <summary>Optional manual employee number override (FR-4 — auto-generated when omitted).</summary>
    public string? EmployeeNo { get; init; }
    public DateTime? DateOfBirth { get; init; }
    public Gender? Gender { get; init; }
}
