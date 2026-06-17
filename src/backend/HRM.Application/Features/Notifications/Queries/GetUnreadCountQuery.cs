using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Notifications.DTOs;
using MediatR;

namespace HRM.Application.Features.Notifications.Queries;

/// <summary>US-NTF-001 FR-5: the caller's unread-notification count for the bell badge.</summary>
public sealed record GetUnreadCountQuery : IRequest<Result<UnreadCountDto>>;

public sealed class GetUnreadCountQueryHandler
    : IRequestHandler<GetUnreadCountQuery, Result<UnreadCountDto>>
{
    private readonly INotificationReadService _service;

    public GetUnreadCountQueryHandler(INotificationReadService service) => _service = service;

    public Task<Result<UnreadCountDto>> Handle(GetUnreadCountQuery request, CancellationToken cancellationToken)
        => _service.GetUnreadCountAsync(cancellationToken);
}
