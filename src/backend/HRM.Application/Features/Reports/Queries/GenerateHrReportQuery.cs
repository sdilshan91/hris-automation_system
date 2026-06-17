using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Reports.DTOs;
using MediatR;

namespace HRM.Application.Features.Reports.Queries;

/// <summary>
/// Generates a pre-built HR report (JSON payload) for the given KEBAB type key + filters (US-RPT-001
/// FR-1/FR-2). <see cref="Type"/> is the raw kebab route value (validated + parsed here); <see cref="Refresh"/>
/// is the [FromQuery] cache-bypass flag (FR-8).
/// </summary>
public sealed record GenerateHrReportQuery(
    string Type,
    HrReportQueryParams QueryParams,
    bool Refresh = false) : IRequest<Result<HrReportResult>>;

public sealed class GenerateHrReportQueryHandler
    : IRequestHandler<GenerateHrReportQuery, Result<HrReportResult>>
{
    private readonly IHrReportService _service;

    public GenerateHrReportQueryHandler(IHrReportService service) => _service = service;

    public Task<Result<HrReportResult>> Handle(GenerateHrReportQuery request, CancellationToken cancellationToken)
    {
        // The validator has already rejected an unknown kebab key; parse for the service.
        if (!HrReportTypeKey.TryParse(request.Type, out var reportType))
            return Task.FromResult(Result<HrReportResult>.Failure($"Unknown report type '{request.Type}'.", 400));

        return _service.GenerateReportAsync(reportType, request.QueryParams, request.Refresh, cancellationToken);
    }
}
