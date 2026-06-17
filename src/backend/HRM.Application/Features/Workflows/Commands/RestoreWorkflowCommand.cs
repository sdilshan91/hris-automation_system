using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Workflows.DTOs;
using MediatR;

namespace HRM.Application.Features.Workflows.Commands;

/// <summary>US-ADM-007 FR-6: restore an archived workflow (auto-archives any active one for its type, BR-2).</summary>
public sealed record RestoreWorkflowCommand(Guid Id) : IRequest<Result<WorkflowDetailDto>>;

public sealed class RestoreWorkflowCommandHandler
    : IRequestHandler<RestoreWorkflowCommand, Result<WorkflowDetailDto>>
{
    private readonly IWorkflowService _service;

    public RestoreWorkflowCommandHandler(IWorkflowService service) => _service = service;

    public Task<Result<WorkflowDetailDto>> Handle(RestoreWorkflowCommand request, CancellationToken cancellationToken)
        => _service.RestoreAsync(request.Id, cancellationToken);
}
