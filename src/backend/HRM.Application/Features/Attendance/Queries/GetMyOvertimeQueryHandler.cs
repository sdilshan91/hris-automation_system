using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

public sealed class GetMyOvertimeQueryHandler
    : IRequestHandler<GetMyOvertimeQuery, Result<IReadOnlyList<OvertimeDto>>>
{
    private readonly IOvertimeService _service;

    public GetMyOvertimeQueryHandler(IOvertimeService service)
    {
        _service = service;
    }

    public Task<Result<IReadOnlyList<OvertimeDto>>> Handle(
        GetMyOvertimeQuery request, CancellationToken cancellationToken)
    {
        return _service.GetMyOvertimeAsync(cancellationToken);
    }
}
