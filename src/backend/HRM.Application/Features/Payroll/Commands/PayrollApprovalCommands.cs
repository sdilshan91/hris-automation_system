using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Commands;

// ════════════════════════════════════════════════════════════════════════
//  US-PAY-008 — Payroll approval-workflow commands. Thin: each delegates to
//  IPayrollApprovalService. The originating IP (FR-7) is resolved in the controller from the request
//  context and passed through.
// ════════════════════════════════════════════════════════════════════════

/// <summary>AC-1: ReviewPending -&gt; AwaitingApproval (BR-3 re-submit also from Rejected).</summary>
public sealed record SubmitPayrollForApprovalCommand(
    Guid RunId, int? TotalApprovalSteps, string? Comments, string? IpAddress
) : IRequest<Result<PayrollApprovalResultDto>>;

public sealed class SubmitPayrollForApprovalCommandHandler
    : IRequestHandler<SubmitPayrollForApprovalCommand, Result<PayrollApprovalResultDto>>
{
    private readonly IPayrollApprovalService _service;
    public SubmitPayrollForApprovalCommandHandler(IPayrollApprovalService service) => _service = service;

    public Task<Result<PayrollApprovalResultDto>> Handle(SubmitPayrollForApprovalCommand request, CancellationToken cancellationToken)
        => _service.SubmitForApprovalAsync(request.RunId, request.TotalApprovalSteps, request.Comments, request.IpAddress, cancellationToken);
}

/// <summary>AC-2/AC-4: approve (advance step or finalize the approval) with BR-5 maker-checker.</summary>
public sealed record ApprovePayrollRunCommand(
    Guid RunId, string? Comments, string? IpAddress
) : IRequest<Result<PayrollApprovalResultDto>>;

public sealed class ApprovePayrollRunCommandHandler
    : IRequestHandler<ApprovePayrollRunCommand, Result<PayrollApprovalResultDto>>
{
    private readonly IPayrollApprovalService _service;
    public ApprovePayrollRunCommandHandler(IPayrollApprovalService service) => _service = service;

    public Task<Result<PayrollApprovalResultDto>> Handle(ApprovePayrollRunCommand request, CancellationToken cancellationToken)
        => _service.ApproveAsync(request.RunId, request.Comments, request.IpAddress, cancellationToken);
}

/// <summary>AC-3: reject with a required reason.</summary>
public sealed record RejectPayrollRunCommand(
    Guid RunId, string Reason, string? IpAddress
) : IRequest<Result<PayrollApprovalResultDto>>;

public sealed class RejectPayrollRunCommandHandler
    : IRequestHandler<RejectPayrollRunCommand, Result<PayrollApprovalResultDto>>
{
    private readonly IPayrollApprovalService _service;
    public RejectPayrollRunCommandHandler(IPayrollApprovalService service) => _service = service;

    public Task<Result<PayrollApprovalResultDto>> Handle(RejectPayrollRunCommand request, CancellationToken cancellationToken)
        => _service.RejectAsync(request.RunId, request.Reason, request.IpAddress, cancellationToken);
}

/// <summary>FR-9: return to HR with required comments (no formal rejection).</summary>
public sealed record ReturnPayrollRunToHrCommand(
    Guid RunId, string Comments, string? IpAddress
) : IRequest<Result<PayrollApprovalResultDto>>;

public sealed class ReturnPayrollRunToHrCommandHandler
    : IRequestHandler<ReturnPayrollRunToHrCommand, Result<PayrollApprovalResultDto>>
{
    private readonly IPayrollApprovalService _service;
    public ReturnPayrollRunToHrCommandHandler(IPayrollApprovalService service) => _service = service;

    public Task<Result<PayrollApprovalResultDto>> Handle(ReturnPayrollRunToHrCommand request, CancellationToken cancellationToken)
        => _service.ReturnToHrAsync(request.RunId, request.Comments, request.IpAddress, cancellationToken);
}

/// <summary>
/// AC-4/FR-2 (BUG-076): atomically REPLACE the tenant's payroll-approval step → role config. Thin delegate to
/// IPayrollApprovalService, which validates contiguity + role/permission and audits the swap.
/// </summary>
public sealed record SetPayrollApprovalStepConfigCommand(
    IReadOnlyList<PayrollApprovalStepConfigItem> Steps, string? IpAddress
) : IRequest<Result<IReadOnlyList<PayrollApprovalStepConfigDto>>>;

public sealed class SetPayrollApprovalStepConfigCommandHandler
    : IRequestHandler<SetPayrollApprovalStepConfigCommand, Result<IReadOnlyList<PayrollApprovalStepConfigDto>>>
{
    private readonly IPayrollApprovalService _service;
    public SetPayrollApprovalStepConfigCommandHandler(IPayrollApprovalService service) => _service = service;

    public Task<Result<IReadOnlyList<PayrollApprovalStepConfigDto>>> Handle(SetPayrollApprovalStepConfigCommand request, CancellationToken cancellationToken)
        => _service.SetApprovalStepConfigAsync(request.Steps, request.IpAddress, cancellationToken);
}

/// <summary>AC-5/BR-1/BR-6: Approved -&gt; Finalized (terminal; requires a prior approval step).</summary>
public sealed record FinalizePayrollRunCommand(
    Guid RunId, string? IpAddress
) : IRequest<Result<PayrollApprovalResultDto>>;

public sealed class FinalizePayrollRunCommandHandler
    : IRequestHandler<FinalizePayrollRunCommand, Result<PayrollApprovalResultDto>>
{
    private readonly IPayrollApprovalService _service;
    public FinalizePayrollRunCommandHandler(IPayrollApprovalService service) => _service = service;

    public Task<Result<PayrollApprovalResultDto>> Handle(FinalizePayrollRunCommand request, CancellationToken cancellationToken)
        => _service.FinalizeAsync(request.RunId, request.IpAddress, cancellationToken);
}
