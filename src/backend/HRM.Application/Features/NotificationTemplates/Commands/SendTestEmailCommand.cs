using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.NotificationTemplates.DTOs;
using MediatR;

namespace HRM.Application.Features.NotificationTemplates.Commands;

/// <summary>
/// US-NTF-002 FR-8: render the effective template for an event against sample data and send it to the given
/// address via the email sender (log-only by default — never a hard SMTP dependency).
/// </summary>
public sealed record SendTestEmailCommand(string EventKey, TestEmailRequest Request)
    : IRequest<Result<TestEmailResultDto>>;

public sealed class SendTestEmailCommandHandler
    : IRequestHandler<SendTestEmailCommand, Result<TestEmailResultDto>>
{
    private readonly INotificationTemplateService _service;

    public SendTestEmailCommandHandler(INotificationTemplateService service) => _service = service;

    public Task<Result<TestEmailResultDto>> Handle(SendTestEmailCommand request, CancellationToken cancellationToken)
        => _service.SendTestEmailAsync(request.EventKey, request.Request, cancellationToken);
}
