using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Commands;

public sealed class AssignLopCommandHandler
    : IRequestHandler<AssignLopCommand, Result<AssignLopResultDto>>
{
    private readonly ILopService _lopService;

    public AssignLopCommandHandler(ILopService lopService)
    {
        _lopService = lopService;
    }

    public Task<Result<AssignLopResultDto>> Handle(
        AssignLopCommand request, CancellationToken cancellationToken)
        => _lopService.AssignLopAsync(request.Request, cancellationToken);
}
