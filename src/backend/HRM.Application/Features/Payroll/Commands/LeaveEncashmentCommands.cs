using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Commands;

/// <summary>
/// HR-triggers a leave encashment (US-PAY-010 AC-3/FR-5): adds an EARNING adjustment for the next payroll run =
/// eligible_days * (monthly_basic / working_days).
/// </summary>
public sealed record ProcessLeaveEncashmentCommand(
    Guid EmployeeId,
    Guid? LeaveTypeId,
    decimal EligibleDays,
    int PayMonth,
    int PayYear,
    bool IsTaxable
) : IRequest<Result<LeaveEncashmentResultDto>>;

public sealed class ProcessLeaveEncashmentCommandHandler
    : IRequestHandler<ProcessLeaveEncashmentCommand, Result<LeaveEncashmentResultDto>>
{
    private readonly ILeaveEncashmentService _service;
    public ProcessLeaveEncashmentCommandHandler(ILeaveEncashmentService service) => _service = service;

    public Task<Result<LeaveEncashmentResultDto>> Handle(ProcessLeaveEncashmentCommand request, CancellationToken cancellationToken)
        => _service.ProcessAsync(
            new LeaveEncashmentInput(
                request.EmployeeId, request.LeaveTypeId, request.EligibleDays,
                request.PayMonth, request.PayYear, request.IsTaxable),
            cancellationToken);
}
