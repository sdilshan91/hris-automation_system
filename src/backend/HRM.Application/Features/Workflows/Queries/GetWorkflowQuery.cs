using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Workflows.DTOs;
using MediatR;

namespace HRM.Application.Features.Workflows.Queries;

/// <summary>US-ADM-007: get one workflow definition (a specific version) with its ordered steps.</summary>
public sealed record GetWorkflowQuery(Guid Id) : IRequest<Result<WorkflowDetailDto>>;

public sealed class GetWorkflowQueryHandler
    : IRequestHandler<GetWorkflowQuery, Result<WorkflowDetailDto>>
{
    private readonly IWorkflowService _service;

    public GetWorkflowQueryHandler(IWorkflowService service) => _service = service;

    public Task<Result<WorkflowDetailDto>> Handle(GetWorkflowQuery request, CancellationToken cancellationToken)
        => _service.GetAsync(request.Id, cancellationToken);
}
