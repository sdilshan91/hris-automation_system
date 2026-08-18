using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

public sealed class GetLopRegisterQueryHandler
    : IRequestHandler<GetLopRegisterQuery, Result<IReadOnlyList<LopRegisterEntryDto>>>
{
    private readonly ILopService _lopService;

    public GetLopRegisterQueryHandler(ILopService lopService)
    {
        _lopService = lopService;
    }

    public Task<Result<IReadOnlyList<LopRegisterEntryDto>>> Handle(
        GetLopRegisterQuery request, CancellationToken cancellationToken)
        => _lopService.GetLopRegisterAsync(
            request.From, request.To, request.EmployeeIds, cancellationToken);
}
