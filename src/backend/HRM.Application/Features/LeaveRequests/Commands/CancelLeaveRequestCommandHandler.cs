using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Commands;

public sealed class CancelLeaveRequestCommandHandler
    : IRequestHandler<CancelLeaveRequestCommand, Result<LeaveCancellationResultDto>>
{
    private readonly ILeaveRequestService _service;

    public CancelLeaveRequestCommandHandler(ILeaveRequestService service)
    {
        _service = service;
    }

    public Task<Result<LeaveCancellationResultDto>> Handle(
        CancelLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        return _service.CancelAsync(request.LeaveRequestId, request.Reason, cancellationToken);
    }
}
