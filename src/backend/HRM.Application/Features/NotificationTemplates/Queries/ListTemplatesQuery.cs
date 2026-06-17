using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.NotificationTemplates.DTOs;
using MediatR;

namespace HRM.Application.Features.NotificationTemplates.Queries;

/// <summary>
/// US-NTF-002 AC-1: list every catalog event for <paramref name="Language"/>, each flagged custom vs. default
/// with its last-modified time.
/// </summary>
public sealed record ListTemplatesQuery(string? Language)
    : IRequest<Result<IReadOnlyList<TemplateListItemDto>>>;

public sealed class ListTemplatesQueryHandler
    : IRequestHandler<ListTemplatesQuery, Result<IReadOnlyList<TemplateListItemDto>>>
{
    private readonly INotificationTemplateService _service;

    public ListTemplatesQueryHandler(INotificationTemplateService service) => _service = service;

    public Task<Result<IReadOnlyList<TemplateListItemDto>>> Handle(
        ListTemplatesQuery request, CancellationToken cancellationToken)
        => _service.ListAsync(request.Language, cancellationToken);
}
