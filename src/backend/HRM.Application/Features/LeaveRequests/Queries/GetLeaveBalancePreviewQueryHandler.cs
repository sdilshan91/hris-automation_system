using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

public sealed class GetLeaveBalancePreviewQueryHandler
    : IRequestHandler<GetLeaveBalancePreviewQuery, Result<LeaveBalancePreviewDto>>
{
    private readonly ILeaveRequestService _service;

    public GetLeaveBalancePreviewQueryHandler(ILeaveRequestService service)
    {
        _service = service;
    }

    public Task<Result<LeaveBalancePreviewDto>> Handle(
        GetLeaveBalancePreviewQuery request, CancellationToken cancellationToken)
    {
        return _service.GetBalancePreviewAsync(
            request.LeaveTypeId, request.StartDate, request.EndDate, request.IsHalfDay, cancellationToken);
    }
}
