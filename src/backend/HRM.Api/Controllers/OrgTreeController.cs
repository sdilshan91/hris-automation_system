using HRM.Application.DTOs;
using HRM.Application.Features.OrganizationTree.DTOs;
using HRM.Application.Features.OrganizationTree.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped organization tree endpoints (US-CHR-006).
/// Read-only visualization of department hierarchy and reporting structure.
/// </summary>
[ApiController]
[Route("api/v1/tenant/org-tree")]
[Authorize]
public sealed class OrgTreeController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrgTreeController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/tenant/org-tree?view=department|reporting&amp;parentId=&amp;depth=&amp;includeInactive=
    /// Returns the organization tree (US-CHR-006 FR-1, FR-5, FR-6, FR-8).
    /// Department view: hierarchy with employee counts per department node.
    /// Reporting view: manager-to-direct-report relationships (best-effort; full reporting chain deferred to US-CHR-011).
    /// Supports lazy loading via parentId + depth (AC-5, FR-6).
    /// </summary>
    [HttpGet]
    [RequirePermission("Department.View")]
    [ProducesResponseType(typeof(ApiResponse<OrgTreeResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrgTree(
        [FromQuery] string view = "department",
        [FromQuery] Guid? parentId = null,
        [FromQuery] int depth = 2,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = new GetOrgTreeQuery(
            View: view,
            ParentId: parentId,
            Depth: depth,
            IncludeInactive: includeInactive);

        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<OrgTreeResult>.Ok(result.Value!));
    }
}
