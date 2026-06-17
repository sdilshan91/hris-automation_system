using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using HRM.Application.Features.Dashboard.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Role-based KPI dashboard (US-RPT-005). The DEFAULT landing page for every authenticated user (§10). The
/// role (hr / manager / employee) and its widget set are derived SERVER-SIDE from the authenticated user
/// (ICurrentUser) — a client never supplies a role (FR-8). Widget data is tenant-scoped via the EF global
/// query filter + RLS (AC-5) and gated by the user's role/permissions (FR-8).
///
/// <para>Rooted at <c>api/v1/dashboard</c>; gated only with <c>[Authorize]</c> (any authenticated user) since
/// the widget SET itself is the authorization boundary — each role only ever receives its own widgets.</para>
/// </summary>
[ApiController]
[Route("api/v1/dashboard")]
[Authorize]
public sealed class DashboardController : ControllerBase
{
    private readonly IDashboardService _service;

    public DashboardController(IDashboardService service) => _service = service;

    /// <summary>
    /// GET /api/v1/dashboard/widgets?refresh=false
    /// Returns the ordered, role-appropriate widget set for the current user (AC-1/AC-2/AC-3, FR-1/FR-3).
    /// Set <c>?refresh=true</c> to bypass the FR-4 widget cache and recompute. Wrapped in the standard
    /// ApiResponse&lt;T&gt; envelope (the FE unwraps globally).
    /// </summary>
    [HttpGet("widgets")]
    [ProducesResponseType(typeof(ApiResponse<DashboardResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetWidgets(
        [FromQuery] bool refresh,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetWidgetsAsync(refresh, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<DashboardResponse>.Ok(result.Value!));
    }
}
