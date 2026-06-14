using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

public sealed class ApproveOvertimeCommandHandler
    : IRequestHandler<ApproveOvertimeCommand, Result<OvertimeDecisionDto>>
{
    private readonly IOvertimeService _service;

    public ApproveOvertimeCommandHandler(IOvertimeService service)
    {
        _service = service;
    }

    public Task<Result<OvertimeDecisionDto>> Handle(
        ApproveOvertimeCommand request, CancellationToken cancellationToken)
    {
        return _service.ApproveAsync(
            request.OvertimeId, request.ApprovedMinutes, request.Comment, cancellationToken);
    }
}
