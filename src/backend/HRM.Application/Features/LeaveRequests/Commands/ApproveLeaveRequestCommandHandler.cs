using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Commands;

public sealed class ApproveLeaveRequestCommandHandler
    : IRequestHandler<ApproveLeaveRequestCommand, Result<LeaveApprovalResultDto>>
{
    private readonly ILeaveRequestService _service;

    public ApproveLeaveRequestCommandHandler(ILeaveRequestService service)
    {
        _service = service;
    }

    public Task<Result<LeaveApprovalResultDto>> Handle(
        ApproveLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        return _service.ApproveAsync(request.LeaveRequestId, request.Comment, cancellationToken);
    }
}
