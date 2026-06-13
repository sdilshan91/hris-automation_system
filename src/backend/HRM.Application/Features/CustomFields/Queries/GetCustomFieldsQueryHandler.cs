using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.CustomFields.DTOs;
using MediatR;

namespace HRM.Application.Features.CustomFields.Queries;

public sealed class GetCustomFieldsQueryHandler
    : IRequestHandler<GetCustomFieldsQuery, Result<IReadOnlyList<CustomFieldDefinitionListResult>>>
{
    private readonly ICustomFieldService _service;

    public GetCustomFieldsQueryHandler(ICustomFieldService service)
    {
        _service = service;
    }

    public Task<Result<IReadOnlyList<CustomFieldDefinitionListResult>>> Handle(
        GetCustomFieldsQuery request, CancellationToken cancellationToken)
    {
        return _service.GetAllAsync(request.EntityType, cancellationToken);
    }
}
