using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveCarryForward.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveCarryForward.Queries;

public sealed class GetCarryForwardPreviewQueryHandler
    : IRequestHandler<GetCarryForwardPreviewQuery, Result<IReadOnlyList<CarryForwardPreviewDto>>>
{
    private readonly ILeaveCarryForwardService _service;

    public GetCarryForwardPreviewQueryHandler(ILeaveCarryForwardService service)
    {
        _service = service;
    }

    public Task<Result<IReadOnlyList<CarryForwardPreviewDto>>> Handle(
        GetCarryForwardPreviewQuery request, CancellationToken cancellationToken)
    {
        // Reuse the same calendar-year convention as US-LV-006/007 (year ?? current year).
        // TODO(tenant-settings): derive the closing leave year from tenant fiscal-year config
        //   when one exists, instead of the calendar year.
        int fromYear = request.Year ?? DateTime.UtcNow.Year;
        return _service.PreviewYearEndAsync(fromYear, cancellationToken);
    }
}
