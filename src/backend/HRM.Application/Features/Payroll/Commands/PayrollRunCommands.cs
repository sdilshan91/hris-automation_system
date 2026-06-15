using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Commands;

/// <summary>Initiates a monthly payroll run (US-PAY-003 AC-1/FR-1/FR-9).</summary>
public sealed record InitiatePayrollRunCommand(
    int PayMonth,
    int PayYear,
    string? IdempotencyKey
) : IRequest<Result<PayrollRunAcceptedDto>>;

public sealed class InitiatePayrollRunCommandHandler : IRequestHandler<InitiatePayrollRunCommand, Result<PayrollRunAcceptedDto>>
{
    private readonly IPayrollRunService _service;
    public InitiatePayrollRunCommandHandler(IPayrollRunService service) => _service = service;

    public Task<Result<PayrollRunAcceptedDto>> Handle(InitiatePayrollRunCommand request, CancellationToken cancellationToken)
        => _service.InitiateAsync(
            new InitiatePayrollRunInput(request.PayMonth, request.PayYear, request.IdempotencyKey),
            cancellationToken);
}
