using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.CustomFields.DTOs;
using MediatR;

namespace HRM.Application.Features.CustomFields.Commands;

public sealed class UpdateCustomFieldCommandHandler
    : IRequestHandler<UpdateCustomFieldCommand, Result<CustomFieldDefinitionDto>>
{
    private readonly ICustomFieldService _service;

    public UpdateCustomFieldCommandHandler(ICustomFieldService service)
    {
        _service = service;
    }

    public Task<Result<CustomFieldDefinitionDto>> Handle(
        UpdateCustomFieldCommand request, CancellationToken cancellationToken)
    {
        var updateRequest = new UpdateCustomFieldRequest
        {
            FieldName = request.FieldName,
            IsRequired = request.IsRequired,
            Options = request.Options,
            DisplayOrder = request.DisplayOrder,
        };

        return _service.UpdateAsync(request.CustomFieldId, updateRequest, cancellationToken);
    }
}
