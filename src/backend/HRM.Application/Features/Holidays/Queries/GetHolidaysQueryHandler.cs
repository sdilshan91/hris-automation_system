using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Holidays.DTOs;
using MediatR;

namespace HRM.Application.Features.Holidays.Queries;

public sealed class GetHolidaysQueryHandler
    : IRequestHandler<GetHolidaysQuery, Result<IReadOnlyList<HolidayDto>>>
{
    private readonly IHolidayService _holidayService;

    public GetHolidaysQueryHandler(IHolidayService holidayService)
    {
        _holidayService = holidayService;
    }

    public Task<Result<IReadOnlyList<HolidayDto>>> Handle(
        GetHolidaysQuery request, CancellationToken cancellationToken)
        => _holidayService.GetAllAsync(
            request.From, request.To, request.Year, request.LocationId, request.ActiveOnly, cancellationToken);
}
