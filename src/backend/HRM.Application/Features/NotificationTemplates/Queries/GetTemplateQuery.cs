using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.NotificationTemplates.DTOs;
using MediatR;

namespace HRM.Application.Features.NotificationTemplates.Queries;

/// <summary>
/// US-NTF-002 AC-2: the effective template for an event + language, with the catalog's available placeholders
/// and sample data for the reference panel + live preview.
/// </summary>
public sealed record GetTemplateQuery(string EventKey, string? Language)
    : IRequest<Result<TemplateDetailDto>>;

public sealed class GetTemplateQueryHandler
    : IRequestHandler<GetTemplateQuery, Result<TemplateDetailDto>>
{
    private readonly INotificationTemplateService _service;

    public GetTemplateQueryHandler(INotificationTemplateService service) => _service = service;

    public Task<Result<TemplateDetailDto>> Handle(GetTemplateQuery request, CancellationToken cancellationToken)
        => _service.GetAsync(request.EventKey, request.Language, cancellationToken);
}
