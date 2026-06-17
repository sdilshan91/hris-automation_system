using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.AuditLog.DTOs;
using MediatR;

namespace HRM.Application.Features.AuditLog.Commands;

/// <summary>
/// US-ADM-008 AC-4/BR-4/FR-5: export the SAME-filtered audit records as CSV or JSON (masked). Modeled as a
/// command because it WRITES an "AuditLog.Export" audit row (BR-4), not just a read.
/// </summary>
public sealed record ExportAuditLogsCommand(
    AuditLogExportFormat Format,
    DateTime? StartDate,
    DateTime? EndDate,
    Guid? ActorUserId,
    string? Action,
    string? ResourceType,
    string? SearchQuery
) : IRequest<Result<AuditLogExportResult>>;

public sealed class ExportAuditLogsCommandHandler
    : IRequestHandler<ExportAuditLogsCommand, Result<AuditLogExportResult>>
{
    private readonly IAuditLogService _service;

    public ExportAuditLogsCommandHandler(IAuditLogService service) => _service = service;

    public Task<Result<AuditLogExportResult>> Handle(
        ExportAuditLogsCommand request, CancellationToken cancellationToken)
        => _service.ExportAsync(
            new AuditLogFilter(
                request.StartDate, request.EndDate, request.ActorUserId,
                request.Action, request.ResourceType, request.SearchQuery),
            request.Format, cancellationToken);
}
