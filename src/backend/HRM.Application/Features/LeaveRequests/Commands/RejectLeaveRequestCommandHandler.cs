using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Commands;

public sealed class RejectLeaveRequestCommandHandler
    : IRequestHandler<RejectLeaveRequestCommand, Result<LeaveApprovalResultDto>>
{
    private readonly ILeaveRequestService _service;

    public RejectLeaveRequestCommandHandler(ILeaveRequestService service)
    {
        _service = service;
    }

    public Task<Result<LeaveApprovalResultDto>> Handle(
        RejectLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        return _service.RejectAsync(request.LeaveRequestId, request.Reason, cancellationToken);
    }
}
