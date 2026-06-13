using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveTypes.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveTypes.Queries;

public sealed class GetLeaveTypeByIdQueryHandler
    : IRequestHandler<GetLeaveTypeByIdQuery, Result<LeaveTypeDto>>
{
    private readonly ILeaveTypeService _leaveTypeService;

    public GetLeaveTypeByIdQueryHandler(ILeaveTypeService leaveTypeService)
    {
        _leaveTypeService = leaveTypeService;
    }

    public Task<Result<LeaveTypeDto>> Handle(
        GetLeaveTypeByIdQuery request, CancellationToken cancellationToken)
    {
        return _leaveTypeService.GetByIdAsync(request.LeaveTypeId, cancellationToken);
    }
}
