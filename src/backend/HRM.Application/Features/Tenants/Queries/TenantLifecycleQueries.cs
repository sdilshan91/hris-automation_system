using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Tenants.DTOs;
using MediatR;

namespace HRM.Application.Features.Tenants.Queries;

/// <summary>
/// Lists the lifecycle-event history for a tenant (US-ADM-004). Viewable by System Admin AND System Support
/// (BR-7: System Support may VIEW history but not initiate transitions).
/// </summary>
public sealed record GetTenantLifecycleHistoryQuery(Guid TenantId)
    : IRequest<Result<IReadOnlyList<TenantLifecycleEventDto>>>;

public sealed class GetTenantLifecycleHistoryQueryHandler
    : IRequestHandler<GetTenantLifecycleHistoryQuery, Result<IReadOnlyList<TenantLifecycleEventDto>>>
{
    private readonly ITenantLifecycleService _service;
    public GetTenantLifecycleHistoryQueryHandler(ITenantLifecycleService service) => _service = service;

    public Task<Result<IReadOnlyList<TenantLifecycleEventDto>>> Handle(
        GetTenantLifecycleHistoryQuery request, CancellationToken cancellationToken)
        => _service.GetLifecycleHistoryAsync(request.TenantId, cancellationToken);
}
