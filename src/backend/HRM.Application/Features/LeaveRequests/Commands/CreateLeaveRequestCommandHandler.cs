using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Common.Observability;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Commands;

public sealed class CreateLeaveRequestCommandHandler
    : IRequestHandler<CreateLeaveRequestCommand, Result<LeaveRequestDto>>
{
    private readonly ILeaveRequestService _service;

    public CreateLeaveRequestCommandHandler(ILeaveRequestService service)
    {
        _service = service;
    }

    public async Task<Result<LeaveRequestDto>> Handle(
        CreateLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(new CreateLeaveRequestRequest
        {
            LeaveTypeId = request.LeaveTypeId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsHalfDay = request.IsHalfDay,
            HalfDaySession = request.HalfDaySession,
            Reason = request.Reason,
            Attachments = request.Attachments,
            ConfirmLop = request.ConfirmLop,
        }, cancellationToken);

        // US-PLT-004 (item 3): leave-request-submitted meter. Count only actual submissions (success),
        // not validation rejections. Inert when no OTel listener is attached.
        if (result.IsSuccess)
            HrmDomainMetrics.RecordLeaveRequestSubmitted();

        return result;
    }
}
