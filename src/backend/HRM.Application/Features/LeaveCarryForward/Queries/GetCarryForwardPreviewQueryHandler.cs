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

        // BUG-033: bound the year to the same range as the leave-year validators (2000..2100) so an
        // out-of-range value returns a clean 400 instead of throwing ArgumentOutOfRangeException (500)
        // downstream. Matches ComputeEffectiveEntitlementValidator / UpsertLeaveEntitlementOverride.
        if (fromYear is < 2000 or > 2100)
            return Task.FromResult(Result<IReadOnlyList<CarryForwardPreviewDto>>.Failure(
                "Leave year must be between 2000 and 2100.", 400));

        return _service.PreviewYearEndAsync(fromYear, cancellationToken);
    }
}
