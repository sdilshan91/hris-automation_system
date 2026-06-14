using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Commands;

public sealed class OverrideLopCommandHandler
    : IRequestHandler<OverrideLopCommand, Result<OverrideLopResultDto>>
{
    private readonly ILopService _lopService;

    public OverrideLopCommandHandler(ILopService lopService)
    {
        _lopService = lopService;
    }

    public Task<Result<OverrideLopResultDto>> Handle(
        OverrideLopCommand request, CancellationToken cancellationToken)
        => _lopService.OverrideLopAsync(request.LeaveRequestId, request.Request, cancellationToken);
}
