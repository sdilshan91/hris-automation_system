using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Impersonation.DTOs;
using MediatR;

namespace HRM.Application.Features.Impersonation.Queries;

/// <summary>
/// Lists a tenant's active users as impersonation targets for the System Admin picker (US-ADM-003 AC-1).
/// Excludes system-admin / system-tenant users (BR-2).
/// </summary>
public sealed record ListImpersonationTargetsQuery(Guid TargetTenantId)
    : IRequest<Result<IReadOnlyList<ImpersonationTargetDto>>>;

public sealed class ListImpersonationTargetsQueryHandler
    : IRequestHandler<ListImpersonationTargetsQuery, Result<IReadOnlyList<ImpersonationTargetDto>>>
{
    private readonly IImpersonationService _service;

    public ListImpersonationTargetsQueryHandler(IImpersonationService service) => _service = service;

    public Task<Result<IReadOnlyList<ImpersonationTargetDto>>> Handle(
        ListImpersonationTargetsQuery request, CancellationToken cancellationToken)
        => _service.ListTargetsAsync(request.TargetTenantId, cancellationToken);
}
