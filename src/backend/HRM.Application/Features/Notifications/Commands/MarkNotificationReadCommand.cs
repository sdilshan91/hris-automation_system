using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Notifications.Commands;

/// <summary>
/// US-NTF-001 AC-4: mark a single notification read by id. Only the owner's notification can be marked;
/// a non-owned/non-existent id is a 404 (no information leak across users/tenants).
/// </summary>
public sealed record MarkNotificationReadCommand(Guid NotificationId) : IRequest<Result>;

public sealed class MarkNotificationReadCommandHandler
    : IRequestHandler<MarkNotificationReadCommand, Result>
{
    private readonly INotificationReadService _service;

    public MarkNotificationReadCommandHandler(INotificationReadService service) => _service = service;

    public Task<Result> Handle(MarkNotificationReadCommand request, CancellationToken cancellationToken)
        => _service.MarkReadAsync(request.NotificationId, cancellationToken);
}
