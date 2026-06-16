using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Queries;

// ── FR-1 / AC-1: payroll run history ─────────────────────────────────────────

/// <summary>Paginated, filterable, sortable payroll run history (US-PAY-012 FR-1/AC-1).</summary>
public sealed record GetPayrollRunHistoryQuery(PayrollRunHistoryFilter Filter)
    : IRequest<Result<PayrollRunHistoryPageDto>>;

public sealed class GetPayrollRunHistoryQueryHandler
    : IRequestHandler<GetPayrollRunHistoryQuery, Result<PayrollRunHistoryPageDto>>
{
    private readonly IPayrollAuditService _service;
    public GetPayrollRunHistoryQueryHandler(IPayrollAuditService service) => _service = service;

    public Task<Result<PayrollRunHistoryPageDto>> Handle(GetPayrollRunHistoryQuery request, CancellationToken cancellationToken)
        => _service.GetRunHistoryAsync(request.Filter, cancellationToken);
}

// ── FR-4 / AC-4: payroll audit trail ─────────────────────────────────────────

/// <summary>The payroll audit trail filtered by date range / action / actor / resource (US-PAY-012 FR-4/AC-4).</summary>
public sealed record GetPayrollAuditTrailQuery(PayrollAuditTrailFilter Filter)
    : IRequest<Result<PayrollAuditPageDto>>;

public sealed class GetPayrollAuditTrailQueryHandler
    : IRequestHandler<GetPayrollAuditTrailQuery, Result<PayrollAuditPageDto>>
{
    private readonly IPayrollAuditService _service;
    public GetPayrollAuditTrailQueryHandler(IPayrollAuditService service) => _service = service;

    public Task<Result<PayrollAuditPageDto>> Handle(GetPayrollAuditTrailQuery request, CancellationToken cancellationToken)
        => _service.GetAuditTrailAsync(request.Filter, cancellationToken);
}

// ── FR-6 / AC-2: per-run audit timeline ──────────────────────────────────────

/// <summary>The per-run audit timeline in chronological order (US-PAY-012 FR-6/AC-2).</summary>
public sealed record GetPayrollRunAuditTimelineQuery(Guid RunId)
    : IRequest<Result<IReadOnlyList<PayrollAuditEntryDto>>>;

public sealed class GetPayrollRunAuditTimelineQueryHandler
    : IRequestHandler<GetPayrollRunAuditTimelineQuery, Result<IReadOnlyList<PayrollAuditEntryDto>>>
{
    private readonly IPayrollAuditService _service;
    public GetPayrollRunAuditTimelineQueryHandler(IPayrollAuditService service) => _service = service;

    public Task<Result<IReadOnlyList<PayrollAuditEntryDto>>> Handle(GetPayrollRunAuditTimelineQuery request, CancellationToken cancellationToken)
        => _service.GetRunTimelineAsync(request.RunId, cancellationToken);
}

// ── FR-5: audit export ───────────────────────────────────────────────────────

/// <summary>Exports the filtered payroll audit trail to CSV / Excel / PDF (US-PAY-012 FR-5).</summary>
public sealed record ExportPayrollAuditTrailQuery(PayrollAuditTrailFilter Filter, PayrollExportFormat Format)
    : IRequest<Result<PayrollAuditExportResult>>;

public sealed class ExportPayrollAuditTrailQueryHandler
    : IRequestHandler<ExportPayrollAuditTrailQuery, Result<PayrollAuditExportResult>>
{
    private readonly IPayrollAuditService _service;
    public ExportPayrollAuditTrailQueryHandler(IPayrollAuditService service) => _service = service;

    public Task<Result<PayrollAuditExportResult>> Handle(ExportPayrollAuditTrailQuery request, CancellationToken cancellationToken)
        => _service.ExportAuditTrailAsync(request.Filter, request.Format, cancellationToken);
}
