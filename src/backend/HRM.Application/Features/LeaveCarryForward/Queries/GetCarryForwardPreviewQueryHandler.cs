using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveCarryForward.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveCarryForward.Queries;

public sealed class GetCarryForwardPreviewQueryHandler
    : IRequestHandler<GetCarryForwardPreviewQuery, Result<IReadOnlyList<CarryForwardPreviewDto>>>
{
    private readonly ILeaveCarryForwardService _service;
    private readonly ITenantLeaveYearResolver _leaveYearResolver;

    public GetCarryForwardPreviewQueryHandler(
        ILeaveCarryForwardService service, ITenantLeaveYearResolver leaveYearResolver)
    {
        _service = service;
        _leaveYearResolver = leaveYearResolver;
    }

    public async Task<Result<IReadOnlyList<CarryForwardPreviewDto>>> Handle(
        GetCarryForwardPreviewQuery request, CancellationToken cancellationToken)
    {
        // ISSUE-311: an omitted year defaults to the tenant's fiscal leave year (LabelFor), not the raw
        // calendar year — so an Apr–Mar tenant previews the closing leave year that actually applies. An
        // explicit request.Year is used verbatim (BR-5 previous-year selector).
        int fromYear = request.Year
            ?? await _leaveYearResolver.LabelForAsync(
                DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);

        // BUG-033: bound the year to the same range as the leave-year validators (2000..2100) so an
        // out-of-range value returns a clean 400 instead of throwing ArgumentOutOfRangeException (500)
        // downstream. Matches ComputeEffectiveEntitlementValidator / UpsertLeaveEntitlementOverride.
        if (fromYear is < 2000 or > 2100)
            return Result<IReadOnlyList<CarryForwardPreviewDto>>.Failure(
                "Leave year must be between 2000 and 2100.", 400);

        return await _service.PreviewYearEndAsync(fromYear, cancellationToken);
    }
}
