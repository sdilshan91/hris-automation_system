using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.NotificationTemplates.DTOs;
using MediatR;

namespace HRM.Application.Features.NotificationTemplates.Commands;

/// <summary>
/// US-NTF-002 FR-4/AC-2: render the supplied bodies against the catalog's sample data for a live preview. Does
/// not persist. (Modeled as a command because it carries a request body, matching the pinned FE POST contract.)
/// </summary>
public sealed record PreviewTemplateCommand(string EventKey, PreviewTemplateRequest Request)
    : IRequest<Result<RenderedPreviewDto>>;

public sealed class PreviewTemplateCommandHandler
    : IRequestHandler<PreviewTemplateCommand, Result<RenderedPreviewDto>>
{
    private readonly INotificationTemplateService _service;

    public PreviewTemplateCommandHandler(INotificationTemplateService service) => _service = service;

    public Task<Result<RenderedPreviewDto>> Handle(PreviewTemplateCommand request, CancellationToken cancellationToken)
        => _service.PreviewAsync(request.EventKey, request.Request, cancellationToken);
}
