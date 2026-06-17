using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Workflows.DTOs;
using MediatR;

namespace HRM.Application.Features.Workflows.Commands;

/// <summary>US-ADM-007 AC-3/FR-3: edit a workflow — creates a new version when the workflow is active.</summary>
public sealed record UpdateWorkflowCommand(Guid LineageId, UpdateWorkflowRequest Request)
    : IRequest<Result<WorkflowDetailDto>>;

public sealed class UpdateWorkflowCommandHandler
    : IRequestHandler<UpdateWorkflowCommand, Result<WorkflowDetailDto>>
{
    private readonly IWorkflowService _service;

    public UpdateWorkflowCommandHandler(IWorkflowService service) => _service = service;

    public Task<Result<WorkflowDetailDto>> Handle(UpdateWorkflowCommand request, CancellationToken cancellationToken)
        => _service.UpdateAsync(request.LineageId, request.Request, cancellationToken);
}
