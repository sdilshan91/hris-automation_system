using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Workflows.DTOs;
using MediatR;

namespace HRM.Application.Features.Workflows.Queries;

/// <summary>
/// US-ADM-011c FR-10: paged admin list of the runtime instances for a workflow definition lineage
/// (tenant-scoped). pageSize is clamped 1..50 (default 20); page is 1-based.
/// </summary>
public sealed record ListWorkflowInstancesQuery(Guid LineageId, int Page, int PageSize)
    : IRequest<Result<PagedWorkflowInstancesDto>>;

public sealed class ListWorkflowInstancesQueryHandler
    : IRequestHandler<ListWorkflowInstancesQuery, Result<PagedWorkflowInstancesDto>>
{
    private readonly IWorkflowService _service;

    public ListWorkflowInstancesQueryHandler(IWorkflowService service) => _service = service;

    public Task<Result<PagedWorkflowInstancesDto>> Handle(
        ListWorkflowInstancesQuery request, CancellationToken cancellationToken)
        => _service.ListInstancesAsync(request.LineageId, request.Page, request.PageSize, cancellationToken);
}
