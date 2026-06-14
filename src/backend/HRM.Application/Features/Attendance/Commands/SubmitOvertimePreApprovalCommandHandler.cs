using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

public sealed class SubmitOvertimePreApprovalCommandHandler
    : IRequestHandler<SubmitOvertimePreApprovalCommand, Result<OvertimeDto>>
{
    private readonly IOvertimeService _service;

    public SubmitOvertimePreApprovalCommandHandler(IOvertimeService service)
    {
        _service = service;
    }

    public Task<Result<OvertimeDto>> Handle(
        SubmitOvertimePreApprovalCommand request, CancellationToken cancellationToken)
    {
        return _service.SubmitPreApprovalAsync(request.Request, cancellationToken);
    }
}
