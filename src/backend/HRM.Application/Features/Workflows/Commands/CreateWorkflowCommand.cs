using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Workflows.DTOs;
using MediatR;

namespace HRM.Application.Features.Workflows.Commands;

/// <summary>US-ADM-007 AC-2/FR-1: create a new approval workflow definition (v1).</summary>
public sealed record CreateWorkflowCommand(CreateWorkflowRequest Request) : IRequest<Result<WorkflowDetailDto>>;

public sealed class CreateWorkflowCommandHandler
    : IRequestHandler<CreateWorkflowCommand, Result<WorkflowDetailDto>>
{
    private readonly IWorkflowService _service;

    public CreateWorkflowCommandHandler(IWorkflowService service) => _service = service;

    public Task<Result<WorkflowDetailDto>> Handle(CreateWorkflowCommand request, CancellationToken cancellationToken)
        => _service.CreateAsync(request.Request, cancellationToken);
}
