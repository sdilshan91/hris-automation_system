using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

public sealed class RejectRegularizationCommandHandler
    : IRequestHandler<RejectRegularizationCommand, Result<RegularizationDecisionDto>>
{
    private readonly IRegularizationApprovalService _service;

    public RejectRegularizationCommandHandler(IRegularizationApprovalService service)
    {
        _service = service;
    }

    public Task<Result<RegularizationDecisionDto>> Handle(
        RejectRegularizationCommand request, CancellationToken cancellationToken)
    {
        return _service.RejectAsync(request.RegularizationId, request.Reason, cancellationToken);
    }
}
