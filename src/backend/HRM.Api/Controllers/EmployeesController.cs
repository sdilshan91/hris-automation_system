using HRM.Application.DTOs;
using HRM.Application.Features.Employees.Commands;
using HRM.Application.Features.Employees.DTOs;
using HRM.Application.Features.Employees.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

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
            request.EmploymentType, request.Status, request.Location,
            request.CustomFields, request.UserId);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return CreatedAtAction(
            nameof(GetEmployeeById),
            new { id = result.Value!.Id },
            ApiResponse<EmployeeDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/employees/directory
    /// Extended directory search with multi-select filters, sorting, and role-based visibility (US-CHR-003).
    /// </summary>
    [HttpGet("directory")]
    [RequirePermission("Employee.View.Own")]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDirectoryResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDirectory(
        [FromQuery] string? search = null,
        [FromQuery] Guid[]? departmentIds = null,
        [FromQuery] string[]? jobTitles = null,
        [FromQuery] string[]? statuses = null,
        [FromQuery] string[]? employmentTypes = null,
        [FromQuery] string[]? locations = null,
        [FromQuery] DateTime? dateOfJoiningFrom = null,
        [FromQuery] DateTime? dateOfJoiningTo = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool sortDescending = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool showArchived = false,
        CancellationToken cancellationToken = default)
    {
        var query = new GetEmployeeDirectoryQuery(
            Search: search,
            DepartmentIds: departmentIds?.Length > 0 ? departmentIds : null,
            JobTitles: jobTitles?.Length > 0 ? jobTitles : null,
            Statuses: statuses?.Length > 0 ? statuses : null,
            EmploymentTypes: employmentTypes?.Length > 0 ? employmentTypes : null,
            Locations: locations?.Length > 0 ? locations : null,
            DateOfJoiningFrom: dateOfJoiningFrom,
            DateOfJoiningTo: dateOfJoiningTo,
            SortBy: sortBy,
            SortDescending: sortDescending,
            Page: page,
            PageSize: pageSize,
            ShowArchived: showArchived);

        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<EmployeeDirectoryResult>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/employees/directory/export
    /// Exports filtered directory as CSV or Excel (US-CHR-003 FR-8, AC-5, BR-4).
    /// Respects role-based field visibility.
    /// </summary>
    [HttpGet("directory/export")]
    [RequirePermission("Employee.Export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportDirectory(
        [FromQuery, Required] ExportFormat format,
        [FromQuery] string? search = null,
        [FromQuery] Guid[]? departmentIds = null,
        [FromQuery] string[]? jobTitles = null,
        [FromQuery] string[]? statuses = null,
        [FromQuery] string[]? employmentTypes = null,
        [FromQuery] string[]? locations = null,
        [FromQuery] DateTime? dateOfJoiningFrom = null,
        [FromQuery] DateTime? dateOfJoiningTo = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool sortDescending = false,
        [FromQuery] bool showArchived = false,
        CancellationToken cancellationToken = default)
    {
        var query = new ExportEmployeeDirectoryQuery(
            Format: format,
            Search: search,
            DepartmentIds: departmentIds?.Length > 0 ? departmentIds : null,
            JobTitles: jobTitles?.Length > 0 ? jobTitles : null,
            Statuses: statuses?.Length > 0 ? statuses : null,
            EmploymentTypes: employmentTypes?.Length > 0 ? employmentTypes : null,
            Locations: locations?.Length > 0 ? locations : null,
            DateOfJoiningFrom: dateOfJoiningFrom,
            DateOfJoiningTo: dateOfJoiningTo,
            SortBy: sortBy,
            SortDescending: sortDescending,
            ShowArchived: showArchived);

        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        var export = result.Value!;
        return File(export.FileBytes, export.ContentType, export.FileName);
    }

    // ── Document Management (US-CHR-008) ──────────────────────────

    /// <summary>
    /// POST /api/v1/tenant/employees/{employeeId}/documents
    /// Uploads a document for an employee (US-CHR-008 FR-1).
    /// Multipart form with file + metadata (category, description, expiryDate).
    /// </summary>
    [HttpPost("{employeeId:guid}/documents")]
    [RequirePermission("EmployeeDocument.Upload")]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDocumentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [RequestSizeLimit(11 * 1024 * 1024)] // Slightly above 10MB for multipart overhead
    public async Task<IActionResult> UploadDocument(
        Guid employeeId,
        IFormFile file,
        [FromForm] string category,
        [FromForm] string? description = null,
        [FromForm] DateTime? expiryDate = null,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse.Fail("No file uploaded."));

        await using var stream = file.OpenReadStream();
        var command = new UploadEmployeeDocumentCommand(
            employeeId, stream, file.FileName, file.ContentType, file.Length,
            category, description, expiryDate);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<EmployeeDocumentDto>.Ok(result.Value!, "Document uploaded successfully."));
    }

    /// <summary>
    /// GET /api/v1/tenant/employees/{employeeId}/documents
    /// Lists documents for an employee (US-CHR-008 FR-9).
    /// </summary>
    [HttpGet("{employeeId:guid}/documents")]
    [Authorize] // Permission check at service level (EmployeeDocument.View or EmployeeDocument.ViewOwn)
    [ProducesResponseType(typeof(ApiResponse<EmployeeDocumentListResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocuments(
        Guid employeeId,
        [FromQuery] string? category = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetEmployeeDocumentsQuery(employeeId, category);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<EmployeeDocumentListResult>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/employees/{employeeId}/documents/{documentId}/download
    /// Generates a short-lived signed URL for downloading a document (US-CHR-008 FR-6, AC-4).
    /// </summary>
    [HttpGet("{employeeId:guid}/documents/{documentId:guid}/download")]
    [Authorize] // Permission check at service level
    [ProducesResponseType(typeof(ApiResponse<DocumentDownloadResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadDocument(
        Guid employeeId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var query = new GetDocumentDownloadQuery(employeeId, documentId);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<DocumentDownloadResult>.Ok(result.Value!));
    }

    /// <summary>
    /// DELETE /api/v1/tenant/employees/{employeeId}/documents/{documentId}
    /// Soft-deletes a document (US-CHR-008 FR-7). HR Officer only.
    /// </summary>
    [HttpDelete("{employeeId:guid}/documents/{documentId:guid}")]
    [RequirePermission("EmployeeDocument.Delete")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDocument(
        Guid employeeId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var command = new DeleteEmployeeDocumentCommand(employeeId, documentId);
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse.Ok("Document deleted successfully."));
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
