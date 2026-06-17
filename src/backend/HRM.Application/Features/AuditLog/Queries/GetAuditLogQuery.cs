using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.AuditLog.DTOs;
using MediatR;

namespace HRM.Application.Features.AuditLog.Queries;

/// <summary>US-ADM-008 AC-3/FR-2: the full audit record with masked before/after JSON.</summary>
public sealed record GetAuditLogQuery(Guid Id) : IRequest<Result<AuditLogDetailDto>>;

public sealed class GetAuditLogQueryHandler
    : IRequestHandler<GetAuditLogQuery, Result<AuditLogDetailDto>>
{
    private readonly IAuditLogService _service;

    public GetAuditLogQueryHandler(IAuditLogService service) => _service = service;

    public Task<Result<AuditLogDetailDto>> Handle(GetAuditLogQuery request, CancellationToken cancellationToken)
        => _service.GetAsync(request.Id, cancellationToken);
}
