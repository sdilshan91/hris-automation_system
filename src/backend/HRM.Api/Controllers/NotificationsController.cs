using HRM.Application.DTOs;
using HRM.Application.Features.Notifications.Commands;
using HRM.Application.Features.Notifications.DTOs;
using HRM.Application.Features.Notifications.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// In-app notification endpoints for the bell/panel (US-NTF-001). Every action is scoped to the
/// authenticated caller within their resolved tenant — no extra permission gate, since a user always
/// owns their own notifications. Thin: dispatches via MediatR and wraps in <c>ApiResponse&lt;T&gt;</c>.
/// </summary>
[ApiController]
[Route("api/v1/notifications")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotificationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/notifications?page={n}&amp;pageSize={20} — the caller's notifications, most-recent-first,
    /// with unread + total counts (AC-3/FR-6/FR-5).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<NotificationPageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetNotificationsQuery(page, pageSize), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<NotificationPageDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/notifications/unread-count — the caller's unread-count for the bell badge (FR-5).
    /// </summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(ApiResponse<UnreadCountDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetUnreadCountQuery(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<UnreadCountDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/notifications/{id}/read — marks one notification read (AC-4). 404 if not owned/found.
    /// </summary>
    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new MarkNotificationReadCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse.Ok("Notification marked as read."));
    }

    /// <summary>
    /// POST /api/v1/notifications/read-all — marks all of the caller's unread notifications read (AC-5).
    /// </summary>
    [HttpPost("read-all")]
    [ProducesResponseType(typeof(ApiResponse<MarkAllReadResultDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new MarkAllNotificationsReadCommand(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<MarkAllReadResultDto>.Ok(result.Value!));
    }
}
