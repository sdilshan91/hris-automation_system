using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Commands;

public sealed class AssignCompulsoryLeaveCommandHandler
    : IRequestHandler<AssignCompulsoryLeaveCommand, Result<CompulsoryLeaveResultDto>>
{
    private readonly ILopService _lopService;

    public AssignCompulsoryLeaveCommandHandler(ILopService lopService)
    {
        _lopService = lopService;
    }

    public Task<Result<CompulsoryLeaveResultDto>> Handle(
        AssignCompulsoryLeaveCommand request, CancellationToken cancellationToken)
        => _lopService.AssignCompulsoryLeaveAsync(request.Request, cancellationToken);
}
