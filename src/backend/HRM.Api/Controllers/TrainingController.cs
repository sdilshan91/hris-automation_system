using HRM.Application.DTOs;
using HRM.Application.Features.Training.Commands;
using HRM.Application.Features.Training.DTOs;
using HRM.Application.Features.Training.Queries;
using HRM.Domain.Authorization;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped training catalog + enrollment endpoints (US-TRN-001). Reads require any Training.View.*/Manage
/// permission (catalog visibility is further narrowed for non-View.All callers in the service); catalog writes and
/// completion require Training.Manage. All operations are tenant-scoped via the EF global query filter.
/// </summary>
[ApiController]
[Route("api/v1/tenant/training")]
[Authorize]
public sealed class TrainingController : ControllerBase
{
    private readonly IMediator _mediator;

    public TrainingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    // ── Catalog ──────────────────────────────────────────────────────────────

    /// <summary>GET /api/v1/tenant/training/courses — lists courses (AC-3).</summary>
    [HttpGet("courses")]
    [RequirePermission(PermissionCatalog.Training.ViewOwn, PermissionCatalog.Training.ViewAll, PermissionCatalog.Training.Manage)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CourseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCourses(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetCoursesQuery(), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<IReadOnlyList<CourseDto>>.Ok(result.Value!));
    }

    /// <summary>GET /api/v1/tenant/training/courses/{id} — one course (AC-3).</summary>
    [HttpGet("courses/{id:guid}")]
    [RequirePermission(PermissionCatalog.Training.ViewOwn, PermissionCatalog.Training.ViewAll, PermissionCatalog.Training.Manage)]
    [ProducesResponseType(typeof(ApiResponse<CourseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCourseById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetCourseByIdQuery(id), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<CourseDto>.Ok(result.Value!));
    }

    /// <summary>POST /api/v1/tenant/training/courses — creates a Draft course (AC-1).</summary>
    [HttpPost("courses")]
    [RequirePermission(PermissionCatalog.Training.Manage)]
    [ProducesResponseType(typeof(ApiResponse<CourseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateCourse([FromBody] CreateCourseRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreateCourseCommand(request), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return CreatedAtAction(nameof(GetCourseById), new { id = result.Value!.Id }, ApiResponse<CourseDto>.Ok(result.Value!));
    }

    /// <summary>PUT /api/v1/tenant/training/courses/{id} — updates metadata (FR-3).</summary>
    [HttpPut("courses/{id:guid}")]
    [RequirePermission(PermissionCatalog.Training.Manage)]
    [ProducesResponseType(typeof(ApiResponse<CourseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCourse(Guid id, [FromBody] UpdateCourseRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateCourseCommand(id, request), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<CourseDto>.Ok(result.Value!));
    }

    /// <summary>POST /api/v1/tenant/training/courses/{id}/status — status transition (AC-2). Illegal → 409.</summary>
    [HttpPost("courses/{id:guid}/status")]
    [RequirePermission(PermissionCatalog.Training.Manage)]
    [ProducesResponseType(typeof(ApiResponse<CourseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeCourseStatus(Guid id, [FromBody] ChangeCourseStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ChangeCourseStatusCommand(id, request.Status), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<CourseDto>.Ok(result.Value!));
    }

    // ── Enrollments ──────────────────────────────────────────────────────────

    /// <summary>POST /api/v1/tenant/training/courses/{id}/enrollments — enroll (AC-4/AC-5/AC-6).</summary>
    [HttpPost("courses/{id:guid}/enrollments")]
    [RequirePermission(PermissionCatalog.Training.ViewOwn, PermissionCatalog.Training.ViewAll, PermissionCatalog.Training.Manage)]
    [ProducesResponseType(typeof(ApiResponse<EnrollmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Enroll(Guid id, [FromBody] EnrollRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new EnrollCommand(id, request), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<EnrollmentDto>.Ok(result.Value!));
    }

    /// <summary>POST /api/v1/tenant/training/enrollments/{id}/cancel — cancel + FIFO promotion (AC-7).</summary>
    [HttpPost("enrollments/{id:guid}/cancel")]
    [RequirePermission(PermissionCatalog.Training.ViewOwn, PermissionCatalog.Training.ViewAll, PermissionCatalog.Training.Manage)]
    [ProducesResponseType(typeof(ApiResponse<EnrollmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelEnrollment(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CancelEnrollmentCommand(id), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<EnrollmentDto>.Ok(result.Value!));
    }

    /// <summary>POST /api/v1/tenant/training/enrollments/{id}/complete — mark completed (AC-8).</summary>
    [HttpPost("enrollments/{id:guid}/complete")]
    [RequirePermission(PermissionCatalog.Training.Manage)]
    [ProducesResponseType(typeof(ApiResponse<EnrollmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CompleteEnrollment(Guid id, [FromBody] CompleteEnrollmentRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CompleteEnrollmentCommand(id, request), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<EnrollmentDto>.Ok(result.Value!));
    }

    // ── History (AC-9) ───────────────────────────────────────────────────────

    /// <summary>GET /api/v1/tenant/training/me/enrollments — the current user's enrollments (AC-9).</summary>
    [HttpGet("me/enrollments")]
    [RequirePermission(PermissionCatalog.Training.ViewOwn, PermissionCatalog.Training.ViewAll, PermissionCatalog.Training.Manage)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EnrollmentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyEnrollments(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyEnrollmentsQuery(), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<IReadOnlyList<EnrollmentDto>>.Ok(result.Value!));
    }

    /// <summary>GET /api/v1/tenant/training/employees/{employeeId}/training-history — an employee's history (AC-9).</summary>
    [HttpGet("employees/{employeeId:guid}/training-history")]
    [RequirePermission(PermissionCatalog.Training.ViewOwn, PermissionCatalog.Training.ViewAll, PermissionCatalog.Training.Manage)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EnrollmentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTrainingHistory(Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTrainingHistoryQuery(employeeId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<IReadOnlyList<EnrollmentDto>>.Ok(result.Value!));
    }
}
