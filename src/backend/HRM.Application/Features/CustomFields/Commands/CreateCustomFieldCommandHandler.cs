using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.CustomFields.DTOs;
using MediatR;

namespace HRM.Application.Features.CustomFields.Commands;

public sealed class CreateCustomFieldCommandHandler
    : IRequestHandler<CreateCustomFieldCommand, Result<CustomFieldDefinitionDto>>
{
    private readonly ICustomFieldService _service;

    public CreateCustomFieldCommandHandler(ICustomFieldService service)
    {
        _service = service;
    }

    public Task<Result<CustomFieldDefinitionDto>> Handle(
        CreateCustomFieldCommand request, CancellationToken cancellationToken)
    {
        var createRequest = new CreateCustomFieldRequest
        {
            EntityType = request.EntityType,
            FieldName = request.FieldName,
            FieldKey = request.FieldKey,
            FieldType = request.FieldType,
            IsRequired = request.IsRequired,
            Options = request.Options,
            DisplayOrder = request.DisplayOrder,
        };

        return _service.CreateAsync(createRequest, cancellationToken);
    }
}
