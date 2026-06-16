using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Monitoring.DTOs;
using MediatR;

namespace HRM.Application.Features.Monitoring.Queries;

// ── Platform health (AC-1) ──────────────────────────────────────────────────

/// <summary>Platform health summary for the System Admin monitoring dashboard (US-ADM-002 AC-1).</summary>
public sealed record GetPlatformHealthQuery : IRequest<Result<PlatformHealthDto>>;

public sealed class GetPlatformHealthQueryHandler
    : IRequestHandler<GetPlatformHealthQuery, Result<PlatformHealthDto>>
{
    private readonly IPlatformMonitoringService _service;
    public GetPlatformHealthQueryHandler(IPlatformMonitoringService service) => _service = service;

    public Task<Result<PlatformHealthDto>> Handle(GetPlatformHealthQuery request, CancellationToken cancellationToken)
        => _service.GetPlatformHealthAsync(cancellationToken);
}

// ── Tenant usage (AC-2/AC-3) ────────────────────────────────────────────────

/// <summary>Per-tenant usage gauges + quota-breach queue, with filters (US-ADM-002 AC-2/FR-4).</summary>
public sealed record GetTenantUsageQuery(TenantUsageFilter Filter)
    : IRequest<Result<TenantUsageDashboardDto>>;

public sealed class GetTenantUsageQueryHandler
    : IRequestHandler<GetTenantUsageQuery, Result<TenantUsageDashboardDto>>
{
    private readonly IPlatformMonitoringService _service;
    public GetTenantUsageQueryHandler(IPlatformMonitoringService service) => _service = service;

    public Task<Result<TenantUsageDashboardDto>> Handle(GetTenantUsageQuery request, CancellationToken cancellationToken)
        => _service.GetTenantUsageAsync(request.Filter, cancellationToken);
}

// ── Tenant detail (AC-4) ────────────────────────────────────────────────────

/// <summary>Per-tenant operational detail view (US-ADM-002 AC-4). Audited as Monitoring.TenantViewed.</summary>
public sealed record GetTenantMonitoringDetailQuery(Guid TenantId)
    : IRequest<Result<TenantMonitoringDetailDto>>;

public sealed class GetTenantMonitoringDetailQueryHandler
    : IRequestHandler<GetTenantMonitoringDetailQuery, Result<TenantMonitoringDetailDto>>
{
    private readonly IPlatformMonitoringService _service;
    public GetTenantMonitoringDetailQueryHandler(IPlatformMonitoringService service) => _service = service;

    public Task<Result<TenantMonitoringDetailDto>> Handle(
        GetTenantMonitoringDetailQuery request, CancellationToken cancellationToken)
        => _service.GetTenantDetailAsync(request.TenantId, cancellationToken);
}
