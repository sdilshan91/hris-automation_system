using HRM.Application.DTOs;
using HRM.Application.Features.Employees.Commands;
using HRM.Application.Features.Employees.DTOs;
using HRM.Application.Features.Employees.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped employee management endpoints (US-CHR-001, US-CHR-002).
/// Requires Employee.* permissions.
/// </summary>
[ApiController]
[Route("api/v1/tenant/employees")]
[Authorize]
public sealed class EmployeesController : ControllerBase
{
    private readonly IMediator _mediator;

    public EmployeesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/tenant/employees
    /// Lists employees for the current tenant with pagination.
    /// </summary>
    [HttpGet]
    [RequirePermission("Employee.View.All")]
    [ProducesResponseType(typeof(ApiResponse<EmployeeListResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEmployees(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? activeOnly = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetEmployeesQuery(page, pageSize, activeOnly, search), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<EmployeeListResult>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/employees/{id}
    /// Gets a single employee by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Employee.View.All")]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEmployeeById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetEmployeeByIdQuery(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<EmployeeDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/employees/{id}/profile
    /// Gets a comprehensive employee profile with all sections (US-CHR-002 AC-1).
    /// Includes emergency contacts, employment history, and concurrency token.
    /// </summary>
    [HttpGet("{id:guid}/profile")]
    [RequirePermission("Employee.View.All")]
    [ProducesResponseType(typeof(ApiResponse<EmployeeProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEmployeeProfile(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetEmployeeProfileQuery(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<EmployeeProfileDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PATCH /api/v1/tenant/employees/{id}/profile
    /// Updates an employee profile with field-level role permissions,
    /// optimistic concurrency, audit logging, and employment history (US-CHR-002 AC-2, AC-3, AC-4, AC-5, AC-6).
    /// Returns 409 Conflict on stale xmin token.
    /// Returns 403 Forbidden if Employee role attempts to edit restricted fields.
    /// </summary>
    [HttpPatch("{id:guid}/profile")]
    [Authorize] // Permission enforcement is done at service level (Employee.Edit OR Employee.Edit.Own)
    [ProducesResponseType(typeof(ApiResponse<EmployeeProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateEmployeeProfile(
        Guid id,
        [FromBody] UpdateEmployeeProfileRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateEmployeeProfileCommand(id, request);
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<EmployeeProfileDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/employees
    /// Creates a new employee (AC-2).
    /// </summary>
    [HttpPost]
    [RequirePermission("Employee.Create")]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateEmployee(
        [FromBody] CreateEmployeeRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateEmployeeCommand(
            request.FirstName, request.LastName, request.Email,
            request.Phone, request.DateOfBirth, request.Gender,
            request.DateOfJoining, request.DepartmentId, request.JobTitleId,
            request.EmploymentType, request.Status, request.CustomFields,
            request.UserId);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return CreatedAtAction(
            nameof(GetEmployeeById),
            new { id = result.Value!.Id },
            ApiResponse<EmployeeDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/employees/{id}/profile-photo
    /// Uploads a profile photo for an employee (AC-4, FR-6).
    /// </summary>
    [HttpPost("{id:guid}/profile-photo")]
    [RequirePermission("Employee.Edit")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [RequestSizeLimit(6 * 1024 * 1024)] // Slightly above 5MB to allow for multipart overhead
    public async Task<IActionResult> UploadProfilePhoto(
        Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse.Fail("No file uploaded."));

        await using var stream = file.OpenReadStream();
        var command = new UploadProfilePhotoCommand(
            id, stream, file.FileName, file.ContentType, file.Length);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<string>.Ok(result.Value!, "Profile photo uploaded successfully."));
    }
}
