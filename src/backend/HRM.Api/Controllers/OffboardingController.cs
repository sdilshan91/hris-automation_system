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
/// Tenant-scoped HR endpoints for offboarding / exit clearance (US-ONB-005). Writes require
/// <c>Onboarding.Manage</c> (reused — there is no dedicated offboarding permission); all operations are
/// tenant-scoped via the EF global query filter and the tenant_id is taken from the resolved session, never
/// user input (FR-8). Account deactivation reuses the existing user-active mechanism; F&amp;F is triggered via a
/// Payroll integration seam (BR-4). Base path: api/v1/offboarding.
/// </summary>
[ApiController]
[Route("api/v1/offboarding")]
[Authorize]
public sealed class OffboardingController : ControllerBase
{
    private readonly IMediator _mediator;

    public OffboardingController(IMediator mediator) => _mediator = mediator;

    /// <summary>POST /api/v1/offboarding/initiate — AC-1/FR-2: initiate offboarding for a departing employee.</summary>
    [HttpPost("initiate")]
    [RequirePermission("Onboarding.Manage")]
    [ProducesResponseType(typeof(ApiResponse<OffboardingInstanceDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Initiate(
        [FromBody] InitiateOffboardingRequest request, CancellationToken cancellationToken)
    {
        if (!TryParseReason(request.Reason, out var reason))
            return BadRequest(ApiResponse.Fail("Reason must be one of: Resignation, Termination, ContractEnd, Retirement.", "invalid_reason"));

        var command = new InitiateOffboardingCommand(
            request.EmployeeId, request.LastWorkingDay, request.OffboardingTemplateId, reason, request.Notes);

        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return CreatedAtAction(
            nameof(GetById), new { id = result.Value!.Id },
            ApiResponse<OffboardingInstanceDto>.Ok(result.Value!));
    }

    /// <summary>GET /api/v1/offboarding/{id} — AC-3/FR-4: the offboarding with clearance dashboard data.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Onboarding.Manage")]
    [ProducesResponseType(typeof(ApiResponse<OffboardingInstanceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOffboardingInstanceQuery(id), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<OffboardingInstanceDto>.Ok(result.Value!));
    }

    /// <summary>GET /api/v1/offboarding?employeeId={id} — the employee's offboarding instance (or 404).</summary>
    [HttpGet]
    [RequirePermission("Onboarding.Manage")]
    [ProducesResponseType(typeof(ApiResponse<OffboardingInstanceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByEmployee(
        [FromQuery] Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOffboardingByEmployeeQuery(employeeId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<OffboardingInstanceDto>.Ok(result.Value!));
    }

    /// <summary>POST /api/v1/offboarding/tasks/{taskId}/clearance — AC-3: record a department clearance decision.</summary>
    [HttpPost("tasks/{taskId:guid}/clearance")]
    [RequirePermission("Onboarding.Manage")]
    [ProducesResponseType(typeof(ApiResponse<OffboardingInstanceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordClearance(
        Guid taskId, [FromBody] RecordClearanceRequest request, CancellationToken cancellationToken)
    {
        if (!TryParseClearanceStatus(request.Status, out var status))
            return BadRequest(ApiResponse.Fail("Status must be 'approved' or 'pending_issues'.", "invalid_status"));

        var result = await _mediator.Send(
            new RecordClearanceCommand(taskId, status, request.Remarks), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<OffboardingInstanceDto>.Ok(result.Value!));
    }

    /// <summary>POST /api/v1/offboarding/tasks/{taskId}/return-asset — AC-2/BR-3: return an asset, complete the task.</summary>
    [HttpPost("tasks/{taskId:guid}/return-asset")]
    [RequirePermission("Onboarding.Manage")]
    [ProducesResponseType(typeof(ApiResponse<OffboardingInstanceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReturnAsset(
        Guid taskId, [FromBody] ReturnAssetRequest request, CancellationToken cancellationToken)
    {
        var condition = TryParseCondition(request.Condition, out var c) ? c : AssetCondition.Good;

        var result = await _mediator.Send(
            new ReturnAssetCommand(taskId, request.AssetId, condition, request.Disposed ?? false), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<OffboardingInstanceDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/offboarding/{id}/complete — AC-4/AC-5: complete offboarding. Returns 409 with the explicit
    /// pending list when mandatory items are outstanding; on success deactivates the account, revokes sessions,
    /// and triggers F&amp;F (FR-5/FR-6/FR-7).
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    [RequirePermission("Onboarding.Manage")]
    [ProducesResponseType(typeof(ApiResponse<CompleteOffboardingResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CompleteOffboardingResultDto>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CompleteOffboardingCommand(id), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        // AC-5: completion blocked by pending mandatory items → 409 with the explicit list in the body.
        if (!result.Value!.Completed)
            return Conflict(ApiResponse<CompleteOffboardingResultDto>.Fail(
                "Cannot complete offboarding. The following mandatory items are pending.", "pending_mandatory_items")
                with { Data = result.Value });

        return Ok(ApiResponse<CompleteOffboardingResultDto>.Ok(result.Value!));
    }

    // ── parsing helpers (accept the contract's snake_case/lowercase string inputs) ──

    private static bool TryParseReason(string? value, out OffboardingReason reason)
    {
        reason = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return Enum.TryParse(value.Replace("_", "").Trim(), ignoreCase: true, out reason)
            && Enum.IsDefined(reason);
    }

    private static bool TryParseClearanceStatus(string? value, out ClearanceStatus status)
    {
        status = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Trim().ToLowerInvariant() switch
        {
            "approved" => Set(ClearanceStatus.Approved, out status),
            "pending_issues" or "pendingissues" => Set(ClearanceStatus.PendingIssues, out status),
            _ => false,
        };
        static bool Set(ClearanceStatus v, out ClearanceStatus s) { s = v; return true; }
    }

    private static bool TryParseCondition(string? value, out AssetCondition condition)
    {
        condition = AssetCondition.Good;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return Enum.TryParse(value.Trim(), ignoreCase: true, out condition) && Enum.IsDefined(condition);
    }
}

/// <summary>POST /initiate body (US-ONB-005 §7). Reason is a string for the FE contract.</summary>
public sealed record InitiateOffboardingRequest(
    Guid EmployeeId,
    DateTime LastWorkingDay,
    Guid? OffboardingTemplateId,
    string Reason,
    string? Notes);

/// <summary>POST /tasks/{taskId}/clearance body — status is "approved" or "pending_issues" (AC-3).</summary>
public sealed record RecordClearanceRequest(string Status, string? Remarks);

/// <summary>POST /tasks/{taskId}/return-asset body (AC-2/BR-3).</summary>
public sealed record ReturnAssetRequest(Guid AssetId, string? Condition, bool? Disposed);
