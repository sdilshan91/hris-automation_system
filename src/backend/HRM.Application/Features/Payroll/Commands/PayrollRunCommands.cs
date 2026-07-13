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

/// <summary>ISSUE-154: cancels a payroll run before finalization (cleans up slips + reverts adjustments).</summary>
public sealed record CancelPayrollRunCommand(Guid RunId) : IRequest<Result<PayrollRunAcceptedDto>>;

public sealed class CancelPayrollRunCommandHandler : IRequestHandler<CancelPayrollRunCommand, Result<PayrollRunAcceptedDto>>
{
    private readonly IPayrollRunService _service;
    public CancelPayrollRunCommandHandler(IPayrollRunService service) => _service = service;

    public Task<Result<PayrollRunAcceptedDto>> Handle(CancelPayrollRunCommand request, CancellationToken cancellationToken)
        => _service.CancelAsync(request.RunId, cancellationToken);
}

/// <summary>ISSUE-154: re-runs a processed-but-not-approved run in place (re-enqueues the processing job).</summary>
public sealed record RerunPayrollRunCommand(Guid RunId) : IRequest<Result<PayrollRunAcceptedDto>>;

public sealed class RerunPayrollRunCommandHandler : IRequestHandler<RerunPayrollRunCommand, Result<PayrollRunAcceptedDto>>
{
    private readonly IPayrollRunService _service;
    public RerunPayrollRunCommandHandler(IPayrollRunService service) => _service = service;

    public Task<Result<PayrollRunAcceptedDto>> Handle(RerunPayrollRunCommand request, CancellationToken cancellationToken)
        => _service.RerunAsync(request.RunId, cancellationToken);
}
