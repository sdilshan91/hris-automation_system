using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

public sealed class GetMyRegularizationsQueryHandler
    : IRequestHandler<GetMyRegularizationsQuery, Result<IReadOnlyList<RegularizationDto>>>
{
    private readonly IAttendanceService _service;

    public GetMyRegularizationsQueryHandler(IAttendanceService service)
    {
        _service = service;
    }

    public Task<Result<IReadOnlyList<RegularizationDto>>> Handle(
        GetMyRegularizationsQuery request, CancellationToken cancellationToken)
    {
        return _service.GetMyRegularizationsAsync(cancellationToken);
    }
}
