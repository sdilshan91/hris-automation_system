using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

public sealed class ApproveRegularizationCommandHandler
    : IRequestHandler<ApproveRegularizationCommand, Result<RegularizationDecisionDto>>
{
    private readonly IRegularizationApprovalService _service;

    public ApproveRegularizationCommandHandler(IRegularizationApprovalService service)
    {
        _service = service;
    }

    public Task<Result<RegularizationDecisionDto>> Handle(
        ApproveRegularizationCommand request, CancellationToken cancellationToken)
    {
        return _service.ApproveAsync(request.RegularizationId, request.Comment, cancellationToken);
    }
}
