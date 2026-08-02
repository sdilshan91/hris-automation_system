using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Queries;

public sealed class ExportAccrualOverCreditExposureQueryHandler
    : IRequestHandler<ExportAccrualOverCreditExposureQuery, Result<PerformanceExportFile>>
{
    private readonly ILeaveEntitlementService _service;

    public ExportAccrualOverCreditExposureQueryHandler(ILeaveEntitlementService service)
    {
        _service = service;
    }

    public Task<Result<PerformanceExportFile>> Handle(
        ExportAccrualOverCreditExposureQuery request, CancellationToken cancellationToken)
    {
        return _service.ExportAccrualOverCreditExposureAsync(
            request.AsOfDate, request.Format, cancellationToken);
    }
}
