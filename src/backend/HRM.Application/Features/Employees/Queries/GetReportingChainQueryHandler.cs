using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

/// <summary>
/// Handler for GetReportingChainQuery (DF-8 / ISSUE-218).
/// Delegates to IReportingStructureService. Mirrors GetDirectReportsQueryHandler.
/// </summary>
public sealed class GetReportingChainQueryHandler
    : IRequestHandler<GetReportingChainQuery, Result<ReportingChainDto>>
{
    private readonly IReportingStructureService _reportingService;

    public GetReportingChainQueryHandler(IReportingStructureService reportingService)
    {
        _reportingService = reportingService;
    }

    public async Task<Result<ReportingChainDto>> Handle(
        GetReportingChainQuery request,
        CancellationToken cancellationToken)
    {
        return await _reportingService.GetReportingChainAsync(request.EmployeeId, cancellationToken);
    }
}
