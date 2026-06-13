using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.CustomFields.Commands;

public sealed class ReactivateCustomFieldCommandHandler
    : IRequestHandler<ReactivateCustomFieldCommand, Result>
{
    private readonly ICustomFieldService _service;

    public ReactivateCustomFieldCommandHandler(ICustomFieldService service)
    {
        _service = service;
    }

    public Task<Result> Handle(
        ReactivateCustomFieldCommand request, CancellationToken cancellationToken)
    {
        return _service.ReactivateAsync(request.CustomFieldId, cancellationToken);
    }
}
