using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.NotificationTemplates.DTOs;
using MediatR;

namespace HRM.Application.Features.NotificationTemplates.Commands;

/// <summary>
/// US-NTF-002 AC-3: upsert the tenant override for an event + language; increments version. Audited via the
/// existing AuditInterceptor.
/// </summary>
public sealed record SaveTemplateCommand(string EventKey, string? Language, TemplateBodyRequest Body)
    : IRequest<Result<TemplateDetailDto>>;

public sealed class SaveTemplateCommandHandler
    : IRequestHandler<SaveTemplateCommand, Result<TemplateDetailDto>>
{
    private readonly INotificationTemplateService _service;

    public SaveTemplateCommandHandler(INotificationTemplateService service) => _service = service;

    public Task<Result<TemplateDetailDto>> Handle(SaveTemplateCommand request, CancellationToken cancellationToken)
        => _service.SaveAsync(request.EventKey, request.Language, request.Body, cancellationToken);
}
