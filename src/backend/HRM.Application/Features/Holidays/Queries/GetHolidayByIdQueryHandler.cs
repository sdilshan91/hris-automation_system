using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Holidays.DTOs;
using MediatR;

namespace HRM.Application.Features.Holidays.Queries;

public sealed class GetHolidayByIdQueryHandler
    : IRequestHandler<GetHolidayByIdQuery, Result<HolidayDto>>
{
    private readonly IHolidayService _holidayService;

    public GetHolidayByIdQueryHandler(IHolidayService holidayService)
    {
        _holidayService = holidayService;
    }

    public Task<Result<HolidayDto>> Handle(
        GetHolidayByIdQuery request, CancellationToken cancellationToken)
        => _holidayService.GetByIdAsync(request.Id, cancellationToken);
}
