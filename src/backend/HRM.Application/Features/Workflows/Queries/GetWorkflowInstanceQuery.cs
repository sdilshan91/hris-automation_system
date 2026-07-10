using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Workflows.DTOs;
using MediatR;

namespace HRM.Application.Features.Workflows.Queries;

/// <summary>
/// US-ADM-011c FR-12: get a live workflow instance with its ordered step chain (tenant-scoped, AC-9). The
/// instance is visible only within its own tenant (the global query filter enforces isolation).
/// </summary>
public sealed record GetWorkflowInstanceQuery(Guid InstanceId)
    : IRequest<Result<WorkflowInstanceDetailDto>>;

public sealed class GetWorkflowInstanceQueryHandler
    : IRequestHandler<GetWorkflowInstanceQuery, Result<WorkflowInstanceDetailDto>>
{
    private readonly IWorkflowService _service;

    public GetWorkflowInstanceQueryHandler(IWorkflowService service) => _service = service;

    public Task<Result<WorkflowInstanceDetailDto>> Handle(
        GetWorkflowInstanceQuery request, CancellationToken cancellationToken)
        => _service.GetInstanceAsync(request.InstanceId, cancellationToken);
}
