using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.OrganizationTree.DTOs;
using MediatR;

namespace HRM.Application.Features.OrganizationTree.Queries;

/// <summary>
/// Handles the GetOrgTreeQuery by delegating to IOrganizationTreeService (US-CHR-006).
/// </summary>
public sealed class GetOrgTreeQueryHandler
    : IRequestHandler<GetOrgTreeQuery, Result<OrgTreeResult>>
{
    private readonly IOrganizationTreeService _orgTreeService;

    public GetOrgTreeQueryHandler(IOrganizationTreeService orgTreeService)
    {
        _orgTreeService = orgTreeService;
    }

    public Task<Result<OrgTreeResult>> Handle(
        GetOrgTreeQuery request, CancellationToken cancellationToken)
    {
        return request.View.Equals("reporting", StringComparison.OrdinalIgnoreCase)
            ? _orgTreeService.GetReportingTreeAsync(request.ParentId, request.Depth, request.IncludeInactive, cancellationToken)
            : _orgTreeService.GetDepartmentTreeAsync(request.ParentId, request.Depth, request.IncludeInactive, cancellationToken);
    }
}
