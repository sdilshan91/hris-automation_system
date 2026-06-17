using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.AuditLog.DTOs;
using MediatR;

namespace HRM.Application.Features.AuditLog.Queries;

/// <summary>
/// US-NTF-005 FR-2: distinct action names + resource types present in THIS tenant's audit log, to populate the
/// multi-select filter dropdowns. Tenant-scoped (the service filters by ITenantContext).
/// </summary>
public sealed record GetAuditLogFilterOptionsQuery
    : IRequest<Result<AuditLogFilterOptionsDto>>;

public sealed class GetAuditLogFilterOptionsQueryHandler
    : IRequestHandler<GetAuditLogFilterOptionsQuery, Result<AuditLogFilterOptionsDto>>
{
    private readonly IAuditLogService _service;

    public GetAuditLogFilterOptionsQueryHandler(IAuditLogService service) => _service = service;

    public Task<Result<AuditLogFilterOptionsDto>> Handle(
        GetAuditLogFilterOptionsQuery request, CancellationToken cancellationToken)
        => _service.GetFilterOptionsAsync(cancellationToken);
}
