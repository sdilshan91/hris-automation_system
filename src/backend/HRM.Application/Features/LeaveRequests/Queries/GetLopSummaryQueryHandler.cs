using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

public sealed class GetLopSummaryQueryHandler
    : IRequestHandler<GetLopSummaryQuery, Result<LopSummaryDto>>
{
    private readonly ILopService _lopService;

    public GetLopSummaryQueryHandler(ILopService lopService)
    {
        _lopService = lopService;
    }

    public Task<Result<LopSummaryDto>> Handle(
        GetLopSummaryQuery request, CancellationToken cancellationToken)
        => _lopService.GetLopSummaryAsync(
            request.EmployeeId, request.From, request.To, cancellationToken);
}
