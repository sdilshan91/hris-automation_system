using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

/// <summary>
/// Handler for GetDirectReportsQuery (US-CHR-011 FR-5, AC-4).
/// Delegates to IReportingStructureService.
/// </summary>
public sealed class GetDirectReportsQueryHandler
    : IRequestHandler<GetDirectReportsQuery, Result<DirectReportsResult>>
{
    private readonly IReportingStructureService _reportingService;

    public GetDirectReportsQueryHandler(IReportingStructureService reportingService)
    {
        _reportingService = reportingService;
    }

    public async Task<Result<DirectReportsResult>> Handle(
        GetDirectReportsQuery request,
        CancellationToken cancellationToken)
    {
        return await _reportingService.GetDirectReportsAsync(request.ManagerId, cancellationToken);
    }
}
