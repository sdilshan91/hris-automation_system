using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Commands;

/// <summary>Creates a new effective-dated payroll calendar policy version (US-ATT-011 AC-4 / CAL-5).</summary>
public sealed record CreatePayrollCalendarPolicyCommand(
    DateOnly EffectiveFrom,
    bool ExcludeHolidaysFromWorkingDays,
    bool IsActive
) : IRequest<Result<PayrollCalendarPolicyDto>>;

public sealed class CreatePayrollCalendarPolicyCommandHandler
    : IRequestHandler<CreatePayrollCalendarPolicyCommand, Result<PayrollCalendarPolicyDto>>
{
    private readonly IPayrollCalendarPolicyService _service;
    public CreatePayrollCalendarPolicyCommandHandler(IPayrollCalendarPolicyService service) => _service = service;

    public Task<Result<PayrollCalendarPolicyDto>> Handle(CreatePayrollCalendarPolicyCommand request, CancellationToken cancellationToken)
        => _service.CreateAsync(new CreatePayrollCalendarPolicyInput(
            request.EffectiveFrom, request.ExcludeHolidaysFromWorkingDays, request.IsActive),
            cancellationToken);
}
