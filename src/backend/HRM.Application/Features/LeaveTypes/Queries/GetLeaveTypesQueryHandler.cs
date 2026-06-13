using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveTypes.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveTypes.Queries;

public sealed class GetLeaveTypesQueryHandler
    : IRequestHandler<GetLeaveTypesQuery, Result<IReadOnlyList<LeaveTypeDto>>>
{
    private readonly ILeaveTypeService _leaveTypeService;

    public GetLeaveTypesQueryHandler(ILeaveTypeService leaveTypeService)
    {
        _leaveTypeService = leaveTypeService;
    }

    public Task<Result<IReadOnlyList<LeaveTypeDto>>> Handle(
        GetLeaveTypesQuery request, CancellationToken cancellationToken)
    {
        return _leaveTypeService.GetAllAsync(request.ActiveOnly, cancellationToken);
    }
}
