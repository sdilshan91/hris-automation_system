using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.LeaveTypes.Commands;

public sealed class DeactivateLeaveTypeCommandHandler
    : IRequestHandler<DeactivateLeaveTypeCommand, Result>
{
    private readonly ILeaveTypeService _leaveTypeService;

    public DeactivateLeaveTypeCommandHandler(ILeaveTypeService leaveTypeService)
    {
        _leaveTypeService = leaveTypeService;
    }

    public Task<Result> Handle(
        DeactivateLeaveTypeCommand request, CancellationToken cancellationToken)
    {
        return _leaveTypeService.DeactivateAsync(request.LeaveTypeId, cancellationToken);
    }
}
