using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Workflows.DTOs;
using MediatR;

namespace HRM.Application.Features.Workflows.Commands;

/// <summary>US-ADM-007 FR-6: archive a workflow (does not count toward the plan limit).</summary>
public sealed record ArchiveWorkflowCommand(Guid Id) : IRequest<Result<WorkflowDetailDto>>;

public sealed class ArchiveWorkflowCommandHandler
    : IRequestHandler<ArchiveWorkflowCommand, Result<WorkflowDetailDto>>
{
    private readonly IWorkflowService _service;

    public ArchiveWorkflowCommandHandler(IWorkflowService service) => _service = service;

    public Task<Result<WorkflowDetailDto>> Handle(ArchiveWorkflowCommand request, CancellationToken cancellationToken)
        => _service.ArchiveAsync(request.Id, cancellationToken);
}
