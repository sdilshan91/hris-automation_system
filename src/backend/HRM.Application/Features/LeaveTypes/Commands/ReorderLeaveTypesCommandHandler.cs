using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.LeaveTypes.Commands;

public sealed class ReorderLeaveTypesCommandHandler
    : IRequestHandler<ReorderLeaveTypesCommand, Result>
{
    private readonly ILeaveTypeService _leaveTypeService;

    public ReorderLeaveTypesCommandHandler(ILeaveTypeService leaveTypeService)
    {
        _leaveTypeService = leaveTypeService;
    }

    public Task<Result> Handle(
        ReorderLeaveTypesCommand request, CancellationToken cancellationToken)
    {
        return _leaveTypeService.ReorderAsync(request.LeaveTypeIds, cancellationToken);
    }
}
