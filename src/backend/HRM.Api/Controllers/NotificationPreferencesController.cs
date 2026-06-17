using System.Globalization;
using HRM.Application.DTOs;
using HRM.Application.Features.NotificationPreferences.Commands;
using HRM.Application.Features.NotificationPreferences.DTOs;
using HRM.Application.Features.NotificationPreferences.Queries;
using HRM.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// US-NTF-003: per-user notification preference endpoints. Every action operates on the CURRENT authenticated
/// user only — no userId is ever accepted from the client (the service resolves it from ICurrentUser). Thin:
/// dispatches via MediatR and wraps results in <c>ApiResponse&lt;T&gt;</c>. No extra permission gate — a user
/// always owns their own preferences.
/// </summary>
[ApiController]
[Route("api/v1/notification-preferences")]
[Authorize]
public sealed class NotificationPreferencesController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotificationPreferencesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>GET /api/v1/notification-preferences — the caller's effective preference matrix + quiet hours (AC-1).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PreferenceMatrixDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPreferences(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPreferencesQuery(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<PreferenceMatrixDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/notification-preferences/{category} — toggle one category's channels (AC-2/AC-3/AC-4).
    /// 422 with a message when the category is mandatory (BR-2) or both channels would be off (BR-3).
    /// </summary>
    [HttpPut("{category}")]
    [ProducesResponseType(typeof(ApiResponse<CategoryPreferenceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateCategory(
        string category,
        [FromBody] UpdateCategoryPreferenceRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<NotificationCategory>(category, ignoreCase: true, out var parsed))
            return StatusCode(404, ApiResponse.Fail($"Unknown notification category '{category}'.", "unknown_category"));

        var result = await _mediator.Send(
            new UpdateCategoryPreferenceCommand(parsed, request.ChannelInApp, request.ChannelEmail),
            cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<CategoryPreferenceDto>.Ok(result.Value!));
    }

    /// <summary>
    /// PUT /api/v1/notification-preferences/quiet-hours — set the caller's quiet-hours window (FR-9/BR-5).
    /// Times are accepted as "HH:mm" or "HH:mm:ss"; timezone is an IANA id.
    /// </summary>
    [HttpPut("quiet-hours")]
    [ProducesResponseType(typeof(ApiResponse<QuietHoursDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateQuietHours(
        [FromBody] UpdateQuietHoursRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParseTime(request.Start, out var start))
            return StatusCode(422, ApiResponse.Fail("Start must be a valid time (HH:mm).", "invalid_time"));
        if (!TryParseTime(request.End, out var end))
            return StatusCode(422, ApiResponse.Fail("End must be a valid time (HH:mm).", "invalid_time"));

        var result = await _mediator.Send(
            new UpdateQuietHoursCommand(request.Enabled, start, end, request.Timezone),
            cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<QuietHoursDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/notification-preferences/reset — delete the caller's overrides, reverting to tenant
    /// defaults; returns the reverted matrix (FR-7).
    /// </summary>
    [HttpPost("reset")]
    [ProducesResponseType(typeof(ApiResponse<PreferenceMatrixDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reset(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ResetPreferencesCommand(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<PreferenceMatrixDto>.Ok(result.Value!));
    }

    /// <summary>Lenient time parse: accepts null/empty (→ null), "HH:mm", or "HH:mm:ss".</summary>
    private static bool TryParseTime(string? value, out TimeOnly? time)
    {
        time = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            || TimeOnly.TryParseExact(value, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)
            || TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
        {
            time = t;
            return true;
        }

        return false;
    }
}

/// <summary>Request body for PUT /{category} (US-NTF-003 AC-2).</summary>
public sealed record UpdateCategoryPreferenceRequest(bool ChannelInApp, bool ChannelEmail);

/// <summary>Request body for PUT /quiet-hours (US-NTF-003 FR-9). Times as "HH:mm"/"HH:mm:ss" strings.</summary>
public sealed record UpdateQuietHoursRequest(bool Enabled, string? Start, string? End, string? Timezone);
