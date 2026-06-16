using HRM.Application.DTOs;
using HRM.Application.Features.Monitoring.DTOs;
using HRM.Application.Features.Monitoring.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// System Admin Console — platform health + tenant-usage monitoring (US-ADM-002). These endpoints run in the
/// SYSTEM/admin context (admin.* subdomain → IsSystemContext, no resolved tenant) and aggregate ACROSS tenants;
/// the service uses IgnoreQueryFilters deliberately and groups by TenantId.
///
/// <para><b>Authorization (BR-1).</b> Gated by the <c>Monitoring.View</c> permission. The platform SystemAdmin
/// role holds it (seeded with every catalog permission). System Support is read-only here by construction —
/// the whole story is read-only (GET endpoints only; no mutation exists to gate). The codebase's JWT carries
/// roles in a custom claim, so the established permission-policy gate is used rather than
/// <c>[Authorize(Roles=...)]</c> — same decision as US-ADM-001's AdminTenantsController.</para>
///
/// <para><b>Real-time.</b> No SignalR hub (deliberately): the FE POLLs these GET endpoints (AC-1 default 30s).</para>
///
/// <para><b>Deferred metrics.</b> Error rate %, P95 latency, 24h trend series, SLA uptime, and storage/API/email
/// usage gauges have NO real data source in this platform (no observability pipeline). They are returned as
/// null/empty with explicit "RequiresObservabilityPipeline" status flags — never fabricated. See the DTOs.</para>
/// </summary>
[ApiController]
[Route("api/v1/system/monitoring")]
[Authorize]
public sealed class AdminMonitoringController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminMonitoringController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/system/monitoring/health — platform health summary (AC-1). Audited Monitoring.Viewed.</summary>
    [HttpGet("health")]
    [RequirePermission("Monitoring.View")]
    [ProducesResponseType(typeof(ApiResponse<PlatformHealthDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetHealth(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPlatformHealthQuery(), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<PlatformHealthDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/system/monitoring/tenant-usage — per-tenant usage gauges + quota-breach queue (AC-2/AC-3),
    /// with FR-4 filters (status, plan, search, created-date range; region + error-rate-threshold are accepted
    /// but DEFERRED no-ops). Audited Monitoring.Viewed.
    /// </summary>
    [HttpGet("tenant-usage")]
    [RequirePermission("Monitoring.View")]
    [ProducesResponseType(typeof(ApiResponse<TenantUsageDashboardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTenantUsage(
        [FromQuery] string? status,
        [FromQuery] string? plan,
        [FromQuery] string? search,
        [FromQuery] DateTime? createdFrom,
        [FromQuery] DateTime? createdTo,
        [FromQuery] string? region,
        [FromQuery] double? errorRateThreshold,
        CancellationToken cancellationToken)
    {
        var filter = new TenantUsageFilter(status, plan, search, createdFrom, createdTo, region, errorRateThreshold);
        var result = await _mediator.Send(new GetTenantUsageQuery(filter), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<TenantUsageDashboardDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/system/monitoring/tenants/{tenantId} — per-tenant operational detail (AC-4). Audited
    /// Monitoring.TenantViewed with ResourceId = tenantId (NFR-5).
    /// </summary>
    [HttpGet("tenants/{tenantId:guid}")]
    [RequirePermission("Monitoring.View")]
    [ProducesResponseType(typeof(ApiResponse<TenantMonitoringDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTenantDetail(Guid tenantId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTenantMonitoringDetailQuery(tenantId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<TenantMonitoringDetailDto>.Ok(result.Value!));
    }
}
