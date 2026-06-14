using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

public sealed class GetPendingRegularizationsQueryHandler
    : IRequestHandler<GetPendingRegularizationsQuery, Result<PendingRegularizationQueueResult>>
{
    private readonly IRegularizationApprovalService _service;

    public GetPendingRegularizationsQueryHandler(IRegularizationApprovalService service)
    {
        _service = service;
    }

    public Task<Result<PendingRegularizationQueueResult>> Handle(
        GetPendingRegularizationsQuery request, CancellationToken cancellationToken)
    {
        return _service.GetPendingForManagerAsync(request.QueryParams, cancellationToken);
    }
}
