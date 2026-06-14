using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Holidays.DTOs;
using MediatR;

namespace HRM.Application.Features.Holidays.Commands;

public sealed class CreateHolidayCommandHandler
    : IRequestHandler<CreateHolidayCommand, Result<HolidayDto>>
{
    private readonly IHolidayService _holidayService;

    public CreateHolidayCommandHandler(IHolidayService holidayService)
    {
        _holidayService = holidayService;
    }

    public Task<Result<HolidayDto>> Handle(
        CreateHolidayCommand request, CancellationToken cancellationToken)
    {
        var createRequest = new CreateHolidayRequest
        {
            Name = request.Name,
            Date = request.Date,
            Type = request.Type,
            LocationId = request.LocationId,
            Description = request.Description,
            IsRecurring = request.IsRecurring,
        };

        return _holidayService.CreateAsync(createRequest, cancellationToken);
    }
}
