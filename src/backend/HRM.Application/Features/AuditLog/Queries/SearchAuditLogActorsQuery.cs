using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.AuditLog.DTOs;
using MediatR;

namespace HRM.Application.Features.AuditLog.Queries;

/// <summary>
/// US-NTF-005 FR-2: actor type-ahead — distinct actors that appear in THIS tenant's audit log and match the
/// search term (by name/email), capped. Tenant-scoped (the service filters by ITenantContext).
/// </summary>
public sealed record SearchAuditLogActorsQuery(
    string? Search,
    int Limit = 20
) : IRequest<Result<IReadOnlyList<AuditLogActorDto>>>;

public sealed class SearchAuditLogActorsQueryHandler
    : IRequestHandler<SearchAuditLogActorsQuery, Result<IReadOnlyList<AuditLogActorDto>>>
{
    private readonly IAuditLogService _service;

    public SearchAuditLogActorsQueryHandler(IAuditLogService service) => _service = service;

    public Task<Result<IReadOnlyList<AuditLogActorDto>>> Handle(
        SearchAuditLogActorsQuery request, CancellationToken cancellationToken)
        => _service.SearchActorsAsync(request.Search, request.Limit, cancellationToken);
}
