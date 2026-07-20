using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Commands;

/// <summary>
/// Generates (or regenerates, AC-5) the PDF payslips for a payroll run (US-PAY-004 AC-1/FR-4). Enqueues the
/// tenant-aware Hangfire batch job. Only ReviewPending / Approved / Finalized runs (BR-1).
/// </summary>
public sealed record GeneratePayslipsCommand(Guid RunId) : IRequest<Result<PayslipGenerationAcceptedDto>>;

public sealed class GeneratePayslipsCommandHandler : IRequestHandler<GeneratePayslipsCommand, Result<PayslipGenerationAcceptedDto>>
{
    private readonly IPayslipGenerationService _service;
    public GeneratePayslipsCommandHandler(IPayslipGenerationService service) => _service = service;

    public Task<Result<PayslipGenerationAcceptedDto>> Handle(GeneratePayslipsCommand request, CancellationToken cancellationToken)
        => _service.GenerateAsync(request.RunId, cancellationToken);
}

/// <summary>
/// Regenerates the PDF payslips for a run (US-PAY-004 AC-5) — overwrites the existing PDFs using the current
/// template. Behaviourally identical to <see cref="GeneratePayslipsCommand"/> (the service resets every slip
/// to Pending and re-enqueues), but exposed as a distinct command/route so the FE "Regenerate" action and the
/// initial "Generate" action are explicit. Same BR-1 status guard applies.
/// </summary>
public sealed record RegeneratePayslipsCommand(Guid RunId) : IRequest<Result<PayslipGenerationAcceptedDto>>;

public sealed class RegeneratePayslipsCommandHandler : IRequestHandler<RegeneratePayslipsCommand, Result<PayslipGenerationAcceptedDto>>
{
    private readonly IPayslipGenerationService _service;
    public RegeneratePayslipsCommandHandler(IPayslipGenerationService service) => _service = service;

    public Task<Result<PayslipGenerationAcceptedDto>> Handle(RegeneratePayslipsCommand request, CancellationToken cancellationToken)
        => _service.GenerateAsync(request.RunId, cancellationToken);
}

/// <summary>
/// Retries the PDF render of ONE slip in a run (US-PAY-004 FR-8 / DF-31 / ISSUE-162) — a PDF re-render only, no
/// payroll recalc. Resets just that slip to Pending and enqueues the single-slip retry job. Same BR-1 status guard
/// as <see cref="GeneratePayslipsCommand"/> (Finalized runs allowed — retrying a failed slip on a Finalized run is
/// the primary use case). 404 <c>payslip_not_found</c> when the slip is not visible for the tenant (AC-4).
/// </summary>
public sealed record RetryPayslipCommand(Guid RunId, Guid EmployeeId) : IRequest<Result<PayslipGenerationAcceptedDto>>;

public sealed class RetryPayslipCommandHandler : IRequestHandler<RetryPayslipCommand, Result<PayslipGenerationAcceptedDto>>
{
    private readonly IPayslipGenerationService _service;
    public RetryPayslipCommandHandler(IPayslipGenerationService service) => _service = service;

    public Task<Result<PayslipGenerationAcceptedDto>> Handle(RetryPayslipCommand request, CancellationToken cancellationToken)
        => _service.RetryOneAsync(request.RunId, request.EmployeeId, cancellationToken);
}
