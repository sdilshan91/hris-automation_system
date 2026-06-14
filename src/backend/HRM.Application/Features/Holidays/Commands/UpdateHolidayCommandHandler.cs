using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Holidays.DTOs;
using MediatR;

namespace HRM.Application.Features.Holidays.Commands;

public sealed class UpdateHolidayCommandHandler
    : IRequestHandler<UpdateHolidayCommand, Result<HolidayDto>>
{
    private readonly IHolidayService _holidayService;

    public UpdateHolidayCommandHandler(IHolidayService holidayService)
    {
        _holidayService = holidayService;
    }

    public Task<Result<HolidayDto>> Handle(
        UpdateHolidayCommand request, CancellationToken cancellationToken)
    {
        var updateRequest = new UpdateHolidayRequest
        {
            Name = request.Name,
            Date = request.Date,
            Type = request.Type,
            LocationId = request.LocationId,
            Description = request.Description,
            IsRecurring = request.IsRecurring,
        };

        return _holidayService.UpdateAsync(request.Id, updateRequest, cancellationToken);
    }
}
