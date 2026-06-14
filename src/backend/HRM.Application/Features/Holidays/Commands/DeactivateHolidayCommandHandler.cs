using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Holidays.Commands;

public sealed class DeactivateHolidayCommandHandler
    : IRequestHandler<DeactivateHolidayCommand, Result>
{
    private readonly IHolidayService _holidayService;

    public DeactivateHolidayCommandHandler(IHolidayService holidayService)
    {
        _holidayService = holidayService;
    }

    public Task<Result> Handle(DeactivateHolidayCommand request, CancellationToken cancellationToken)
        => _holidayService.DeactivateAsync(request.Id, cancellationToken);
}
