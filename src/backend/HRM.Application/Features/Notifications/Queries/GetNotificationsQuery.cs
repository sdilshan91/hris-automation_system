using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Notifications.DTOs;
using MediatR;

namespace HRM.Application.Features.Notifications.Queries;

/// <summary>
/// US-NTF-001 AC-3/FR-6: paginated list of the caller's notifications (most-recent-first, 20/page),
/// plus the unread + total counts for the bell badge.
/// </summary>
public sealed record GetNotificationsQuery(int Page = 1, int PageSize = 20)
    : IRequest<Result<NotificationPageDto>>;

public sealed class GetNotificationsQueryHandler
    : IRequestHandler<GetNotificationsQuery, Result<NotificationPageDto>>
{
    private readonly INotificationReadService _service;

    public GetNotificationsQueryHandler(INotificationReadService service) => _service = service;

    public Task<Result<NotificationPageDto>> Handle(GetNotificationsQuery request, CancellationToken cancellationToken)
        => _service.GetPageAsync(request.Page, request.PageSize, cancellationToken);
}
