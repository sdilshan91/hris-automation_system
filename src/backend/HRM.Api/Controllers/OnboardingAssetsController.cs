using System.Text.Json;
using HRM.Application.DTOs;
using HRM.Application.Features.Onboarding.Commands;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Application.Features.Onboarding.Queries;
using HRM.Domain.Enums;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// US-ONB-004 tenant-scoped asset issuance + register endpoints. The HR register reads and issuance require
/// <c>Onboarding.Manage</c>; <c>assets/me</c> is self-service for the authenticated employee (read-only,
/// BR-6). All operations are tenant-scoped via the EF global query filter and the tenant_id is always taken
/// from the resolved session context, never user input (FR-7). Issuance is atomic (NFR-5).
/// </summary>
[ApiController]
[Route("api/v1/onboarding/assets")]
[Authorize]
public sealed class OnboardingAssetsController : ControllerBase
{
    private readonly IMediator _mediator;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public OnboardingAssetsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/onboarding/assets/available?assetType={type}
    /// FR-3: lists Available assets (optionally filtered by type) for the issuance dropdown.
    /// </summary>
    [HttpGet("available")]
    [RequirePermission("Onboarding.Manage")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AvailableAssetDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Available(
        [FromQuery] string? assetType, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAvailableAssetsQuery(assetType), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<AvailableAssetDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/onboarding/assets?employeeId={id}
    /// AC-4 (HR view): lists the assets currently assigned to a given employee.
    /// </summary>
    [HttpGet]
    [RequirePermission("Onboarding.Manage")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AssetDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ByEmployee(
        [FromQuery] Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetEmployeeAssetsQuery(employeeId), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<AssetDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/onboarding/assets/me
    /// AC-4/BR-6: the logged-in employee's own assigned assets (read-only). Requires only authentication.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AssetDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyAssetsQuery(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<AssetDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/onboarding/assets/issue
    /// FR-5: bulk-issues assets to an employee in one transaction (NFR-5). multipart/form-data with a JSON
    /// "request" field ({ employeeId, taskInstanceId?, assets: [...] }) and an optional "acknowledgment"
    /// file (FR-6/NFR-4). Enforces the available gate (BR-1/FR-3), double-assignment guard (AC-3), and
    /// per-tenant tag/serial uniqueness (BR-3) in the service.
    /// </summary>
    [HttpPost("issue")]
    [RequirePermission("Onboarding.Manage")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AssetDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [RequestSizeLimit(11 * 1024 * 1024)] // 10 MB acknowledgment + multipart overhead (NFR-4).
    public async Task<IActionResult> Issue(
        [FromForm] string request,
        IFormFile? acknowledgment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request))
            return BadRequest(ApiResponse.Fail("The 'request' field is required.", "request_required"));

        IssueAssetsRequestDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<IssueAssetsRequestDto>(request, JsonOptions);
        }
        catch (JsonException)
        {
            return BadRequest(ApiResponse.Fail("The 'request' field is not valid JSON.", "invalid_request_json"));
        }

        if (dto is null)
            return BadRequest(ApiResponse.Fail("The 'request' field is required.", "request_required"));

        if (acknowledgment is not null && acknowledgment.Length == 0)
            return BadRequest(ApiResponse.Fail("Uploaded acknowledgment file is empty."));

        // Parse each condition string to the enum (the contract sends condition as text e.g. "New").
        var lines = new List<IssueAssetCommandLine>(dto.Assets.Count);
        foreach (var a in dto.Assets)
        {
            if (!Enum.TryParse<AssetCondition>(a.Condition, ignoreCase: true, out var condition))
                return BadRequest(ApiResponse.Fail(
                    $"'{a.Condition}' is not a valid condition. Use New, Good, Fair, or Poor.", "invalid_condition"));

            lines.Add(new IssueAssetCommandLine(
                a.AssetId, a.AssetType, a.AssetTag, a.SerialNumber, a.Brand, a.Model,
                condition, a.IssueDate, a.Notes));
        }

        Stream? stream = acknowledgment is not null ? acknowledgment.OpenReadStream() : null;
        try
        {
            var command = new IssueAssetsCommand(
                dto.EmployeeId,
                dto.TaskInstanceId,
                lines,
                stream,
                acknowledgment?.FileName,
                acknowledgment?.ContentType,
                acknowledgment?.Length ?? 0);

            var result = await _mediator.Send(command, cancellationToken);

            if (result.IsFailure)
                return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

            return Ok(ApiResponse<IReadOnlyList<AssetDto>>.Ok(result.Value!));
        }
        finally
        {
            if (stream is not null)
                await stream.DisposeAsync();
        }
    }
}
