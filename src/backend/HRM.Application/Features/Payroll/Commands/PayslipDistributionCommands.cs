using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Commands;

/// <summary>
/// Sends payslip PDFs by email to all employees in a FINALIZED run (US-PAY-011 AC-1/FR-1). Enqueues the
/// tenant-aware Hangfire SendPayslipEmailsJob and returns 202. FR-7/BR-5: when payslips were already sent for
/// the run, <paramref name="ConfirmResend"/> must be true (else 409).
/// </summary>
public sealed record SendPayslipEmailsCommand(Guid RunId, bool ConfirmResend)
    : IRequest<Result<PayslipDistributionAcceptedDto>>;

public sealed class SendPayslipEmailsCommandHandler
    : IRequestHandler<SendPayslipEmailsCommand, Result<PayslipDistributionAcceptedDto>>
{
    private readonly IPayslipDistributionService _service;
    public SendPayslipEmailsCommandHandler(IPayslipDistributionService service) => _service = service;

    public Task<Result<PayslipDistributionAcceptedDto>> Handle(SendPayslipEmailsCommand request, CancellationToken cancellationToken)
        => _service.SendAsync(request.RunId, request.ConfirmResend, cancellationToken);
}

/// <summary>
/// Selectively re-sends payslip emails for a run (US-PAY-011 FR-4): to an explicit set of employees, or — when
/// <paramref name="OnlyFailed"/> is true and no ids are given — to all employees whose last delivery was
/// Failed. Returns 202.
/// </summary>
public sealed record ResendPayslipEmailsCommand(
    Guid RunId, IReadOnlyCollection<Guid>? EmployeeIds, bool OnlyFailed)
    : IRequest<Result<PayslipDistributionAcceptedDto>>;

public sealed class ResendPayslipEmailsCommandHandler
    : IRequestHandler<ResendPayslipEmailsCommand, Result<PayslipDistributionAcceptedDto>>
{
    private readonly IPayslipDistributionService _service;
    public ResendPayslipEmailsCommandHandler(IPayslipDistributionService service) => _service = service;

    public Task<Result<PayslipDistributionAcceptedDto>> Handle(ResendPayslipEmailsCommand request, CancellationToken cancellationToken)
        => _service.ResendAsync(request.RunId, request.EmployeeIds, request.OnlyFailed, cancellationToken);
}
