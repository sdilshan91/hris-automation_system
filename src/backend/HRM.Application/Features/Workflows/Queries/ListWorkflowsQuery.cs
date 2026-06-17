using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Workflows.DTOs;
using MediatR;

namespace HRM.Application.Features.Workflows.Queries;

/// <summary>US-ADM-007 AC-1: list the current tenant's approval workflows, grouped/ordered by entity type.</summary>
public sealed record ListWorkflowsQuery() : IRequest<Result<IReadOnlyList<WorkflowListItemDto>>>;

public sealed class ListWorkflowsQueryHandler
    : IRequestHandler<ListWorkflowsQuery, Result<IReadOnlyList<WorkflowListItemDto>>>
{
    private readonly IWorkflowService _service;

    public ListWorkflowsQueryHandler(IWorkflowService service) => _service = service;

    public Task<Result<IReadOnlyList<WorkflowListItemDto>>> Handle(
        ListWorkflowsQuery request, CancellationToken cancellationToken)
        => _service.ListAsync(cancellationToken);
}
