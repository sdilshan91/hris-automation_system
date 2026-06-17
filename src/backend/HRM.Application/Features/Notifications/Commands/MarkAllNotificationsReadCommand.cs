using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Notifications.DTOs;
using MediatR;

namespace HRM.Application.Features.Notifications.Commands;

/// <summary>US-NTF-001 AC-5: mark ALL of the caller's unread notifications read; returns the count changed.</summary>
public sealed record MarkAllNotificationsReadCommand : IRequest<Result<MarkAllReadResultDto>>;

public sealed class MarkAllNotificationsReadCommandHandler
    : IRequestHandler<MarkAllNotificationsReadCommand, Result<MarkAllReadResultDto>>
{
    private readonly INotificationReadService _service;

    public MarkAllNotificationsReadCommandHandler(INotificationReadService service) => _service = service;

    public Task<Result<MarkAllReadResultDto>> Handle(
        MarkAllNotificationsReadCommand request, CancellationToken cancellationToken)
        => _service.MarkAllReadAsync(cancellationToken);
}
