using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Queries;

public sealed class GetAccrualOverCreditExposureQueryHandler
    : IRequestHandler<GetAccrualOverCreditExposureQuery, Result<AccrualOverCreditExposureReportDto>>
{
    private readonly ILeaveEntitlementService _service;

    public GetAccrualOverCreditExposureQueryHandler(ILeaveEntitlementService service)
    {
        _service = service;
    }

    public Task<Result<AccrualOverCreditExposureReportDto>> Handle(
        GetAccrualOverCreditExposureQuery request, CancellationToken cancellationToken)
    {
        return _service.GetAccrualOverCreditExposureAsync(request.AsOfDate, cancellationToken);
    }
}
