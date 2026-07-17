using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.CustomFields.DTOs;
using MediatR;

namespace HRM.Application.Features.CustomFields.Queries;

/// <summary>
/// ISSUE-206: the ACTIVE custom-field definitions for one entity type, ordered for display. Backs
/// <c>GET /custom-fields/active?entityType=…</c> which the FE create/edit forms call to render dynamic inputs.
/// </summary>
public sealed record GetActiveCustomFieldsQuery(string EntityType)
    : IRequest<Result<IReadOnlyList<CustomFieldDefinitionDto>>>;

public sealed class GetActiveCustomFieldsQueryHandler
    : IRequestHandler<GetActiveCustomFieldsQuery, Result<IReadOnlyList<CustomFieldDefinitionDto>>>
{
    private readonly ICustomFieldService _service;

    public GetActiveCustomFieldsQueryHandler(ICustomFieldService service)
    {
        _service = service;
    }

    public Task<Result<IReadOnlyList<CustomFieldDefinitionDto>>> Handle(
        GetActiveCustomFieldsQuery request, CancellationToken cancellationToken)
    {
        return _service.GetActiveAsync(request.EntityType, cancellationToken);
    }
}
