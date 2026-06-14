using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

public sealed class SubmitRegularizationCommandHandler
    : IRequestHandler<SubmitRegularizationCommand, Result<RegularizationDto>>
{
    private readonly IAttendanceService _service;

    public SubmitRegularizationCommandHandler(IAttendanceService service)
    {
        _service = service;
    }

    public Task<Result<RegularizationDto>> Handle(
        SubmitRegularizationCommand request, CancellationToken cancellationToken)
    {
        return _service.SubmitRegularizationAsync(request.Request, cancellationToken);
    }
}
