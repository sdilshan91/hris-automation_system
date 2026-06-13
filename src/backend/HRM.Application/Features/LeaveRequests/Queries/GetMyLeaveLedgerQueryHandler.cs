using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

public sealed class GetMyLeaveLedgerQueryHandler
    : IRequestHandler<GetMyLeaveLedgerQuery, Result<IReadOnlyList<LeaveLedgerEntryDto>>>
{
    private readonly ILeaveDashboardService _service;

    public GetMyLeaveLedgerQueryHandler(ILeaveDashboardService service)
    {
        _service = service;
    }

    public Task<Result<IReadOnlyList<LeaveLedgerEntryDto>>> Handle(
        GetMyLeaveLedgerQuery request, CancellationToken cancellationToken)
    {
        return _service.GetMyLedgerAsync(request.LeaveTypeId, request.Year, cancellationToken);
    }
}
