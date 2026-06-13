using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.CustomFields.DTOs;
using MediatR;

namespace HRM.Application.Features.CustomFields.Queries;

public sealed class GetCustomFieldByIdQueryHandler
    : IRequestHandler<GetCustomFieldByIdQuery, Result<CustomFieldDefinitionDto>>
{
    private readonly ICustomFieldService _service;

    public GetCustomFieldByIdQueryHandler(ICustomFieldService service)
    {
        _service = service;
    }

    public Task<Result<CustomFieldDefinitionDto>> Handle(
        GetCustomFieldByIdQuery request, CancellationToken cancellationToken)
    {
        return _service.GetByIdAsync(request.CustomFieldId, cancellationToken);
    }
}
