using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.CustomFields.Commands;

public sealed class ReorderCustomFieldsCommandHandler
    : IRequestHandler<ReorderCustomFieldsCommand, Result>
{
    private readonly ICustomFieldService _service;

    public ReorderCustomFieldsCommandHandler(ICustomFieldService service)
    {
        _service = service;
    }

    public Task<Result> Handle(
        ReorderCustomFieldsCommand request, CancellationToken cancellationToken)
    {
        return _service.ReorderAsync(request.EntityType, request.FieldIds, cancellationToken);
    }
}
