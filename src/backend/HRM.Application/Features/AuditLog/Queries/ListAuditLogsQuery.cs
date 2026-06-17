using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.AuditLog.DTOs;
using MediatR;

namespace HRM.Application.Features.AuditLog.Queries;

/// <summary>US-ADM-008 AC-1/AC-2/FR-1: paginated, filterable, tenant-scoped audit list (masked summaries).</summary>
public sealed record ListAuditLogsQuery(
    int Page,
    int PageSize,
    DateTime? StartDate,
    DateTime? EndDate,
    Guid? ActorUserId,
    string? Action,
    string? ResourceType,
    string? SearchQuery,
    // US-NTF-005 FR-2: optional multi-select action/resource-type filters (IN-list). Additive — the singular
    // Action/ResourceType above still work and are folded into the same group by the service.
    IReadOnlyList<string>? Actions = null,
    IReadOnlyList<string>? ResourceTypes = null
) : IRequest<Result<AuditLogPageDto>>;

public sealed class ListAuditLogsQueryHandler
    : IRequestHandler<ListAuditLogsQuery, Result<AuditLogPageDto>>
{
    private readonly IAuditLogService _service;

    public ListAuditLogsQueryHandler(IAuditLogService service) => _service = service;

    public Task<Result<AuditLogPageDto>> Handle(ListAuditLogsQuery request, CancellationToken cancellationToken)
        => _service.ListAsync(
            new AuditLogFilter(
                request.StartDate, request.EndDate, request.ActorUserId,
                request.Action, request.ResourceType, request.SearchQuery,
                request.Actions, request.ResourceTypes),
            request.Page, request.PageSize, cancellationToken);
}
