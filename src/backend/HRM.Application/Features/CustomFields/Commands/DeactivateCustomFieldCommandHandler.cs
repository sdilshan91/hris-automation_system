using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.CustomFields.Commands;

public sealed class DeactivateCustomFieldCommandHandler
    : IRequestHandler<DeactivateCustomFieldCommand, Result>
{
    private readonly ICustomFieldService _service;

    public DeactivateCustomFieldCommandHandler(ICustomFieldService service)
    {
        _service = service;
    }

    public Task<Result> Handle(
        DeactivateCustomFieldCommand request, CancellationToken cancellationToken)
    {
        return _service.DeactivateAsync(request.CustomFieldId, cancellationToken);
    }
}
