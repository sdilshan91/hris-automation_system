using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.LeaveTypes.Commands;

public sealed class ReactivateLeaveTypeCommandHandler
    : IRequestHandler<ReactivateLeaveTypeCommand, Result>
{
    private readonly ILeaveTypeService _leaveTypeService;

    public ReactivateLeaveTypeCommandHandler(ILeaveTypeService leaveTypeService)
    {
        _leaveTypeService = leaveTypeService;
    }

    public Task<Result> Handle(
        ReactivateLeaveTypeCommand request, CancellationToken cancellationToken)
    {
        return _leaveTypeService.ReactivateAsync(request.LeaveTypeId, cancellationToken);
    }
}
