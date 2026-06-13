using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
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

    public Task<Result<LeaveRequestDto>> Handle(
        CreateLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        return _service.CreateAsync(new CreateLeaveRequestRequest
        {
            LeaveTypeId = request.LeaveTypeId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsHalfDay = request.IsHalfDay,
            HalfDaySession = request.HalfDaySession,
            Reason = request.Reason,
            Attachments = request.Attachments,
        }, cancellationToken);
    }
}
