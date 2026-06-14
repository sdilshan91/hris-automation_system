using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

public sealed class GetPendingOvertimeQueryHandler
    : IRequestHandler<GetPendingOvertimeQuery, Result<OvertimeQueueResult>>
{
    private readonly IOvertimeService _service;

    public GetPendingOvertimeQueryHandler(IOvertimeService service)
    {
        _service = service;
    }

    public Task<Result<OvertimeQueueResult>> Handle(
        GetPendingOvertimeQuery request, CancellationToken cancellationToken)
    {
        return _service.GetPendingForManagerAsync(cancellationToken);
    }
}
