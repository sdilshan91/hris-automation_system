using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

public sealed class GetMyLeaveBalanceQueryHandler
    : IRequestHandler<GetMyLeaveBalanceQuery, Result<IReadOnlyList<LeaveBalanceDto>>>
{
    private readonly ILeaveDashboardService _service;

    public GetMyLeaveBalanceQueryHandler(ILeaveDashboardService service)
    {
        _service = service;
    }

    public Task<Result<IReadOnlyList<LeaveBalanceDto>>> Handle(
        GetMyLeaveBalanceQuery request, CancellationToken cancellationToken)
    {
        return _service.GetMyBalancesAsync(request.Year, cancellationToken);
    }
}
