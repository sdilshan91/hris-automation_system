using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.NotificationTemplates.DTOs;
using MediatR;

namespace HRM.Application.Features.NotificationTemplates.Commands;

/// <summary>
/// US-NTF-002 AC-4: remove (soft-delete) the tenant override → revert to the system default. Returns the
/// now-effective (default) template. Audited via the existing AuditInterceptor.
/// </summary>
public sealed record ResetTemplateCommand(string EventKey, string? Language)
    : IRequest<Result<TemplateDetailDto>>;

public sealed class ResetTemplateCommandHandler
    : IRequestHandler<ResetTemplateCommand, Result<TemplateDetailDto>>
{
    private readonly INotificationTemplateService _service;

    public ResetTemplateCommandHandler(INotificationTemplateService service) => _service = service;

    public Task<Result<TemplateDetailDto>> Handle(ResetTemplateCommand request, CancellationToken cancellationToken)
        => _service.ResetAsync(request.EventKey, request.Language, cancellationToken);
}
