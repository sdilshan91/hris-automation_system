using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Commands;

/// <summary>Creates a new effective-dated Full-and-Final settlement policy version (ISSUE-294 Phase 1).</summary>
public sealed record CreateFnFPolicyCommand(
    DateOnly EffectiveFrom,
    bool IncludeProRatedFinalPay,
    bool IncludeStatutory,
    bool IncludeLeaveEncashment,
    bool FinalPeriodOwnedBySettlement,
    bool IsActive
) : IRequest<Result<FnFPolicyDto>>;

public sealed class CreateFnFPolicyCommandHandler : IRequestHandler<CreateFnFPolicyCommand, Result<FnFPolicyDto>>
{
    private readonly IFnFPolicyService _service;
    public CreateFnFPolicyCommandHandler(IFnFPolicyService service) => _service = service;

    public Task<Result<FnFPolicyDto>> Handle(CreateFnFPolicyCommand request, CancellationToken cancellationToken)
        => _service.CreateAsync(new CreateFnFPolicyInput(
            request.EffectiveFrom, request.IncludeProRatedFinalPay, request.IncludeStatutory,
            request.IncludeLeaveEncashment, request.FinalPeriodOwnedBySettlement, request.IsActive),
            cancellationToken);
}
