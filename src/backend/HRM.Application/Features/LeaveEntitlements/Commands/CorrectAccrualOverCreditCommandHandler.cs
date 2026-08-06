using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Commands;

public sealed class CorrectAccrualOverCreditCommandHandler
    : IRequestHandler<CorrectAccrualOverCreditCommand, Result<AccrualOverCreditCorrectionResultDto>>
{
    private readonly ILeaveEntitlementService _service;

    public CorrectAccrualOverCreditCommandHandler(ILeaveEntitlementService service)
    {
        _service = service;
    }

    public Task<Result<AccrualOverCreditCorrectionResultDto>> Handle(
        CorrectAccrualOverCreditCommand request, CancellationToken cancellationToken)
    {
        return _service.CorrectAccrualOverCreditAsync(request.AsOfDate, request.DryRun, cancellationToken);
    }
}
