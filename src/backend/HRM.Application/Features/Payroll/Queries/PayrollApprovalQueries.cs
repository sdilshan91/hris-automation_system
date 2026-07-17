using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Queries;

/// <summary>FR-7: the run's approval timeline, newest-first (US-PAY-008 §8 timeline view).</summary>
public sealed record GetPayrollApprovalHistoryQuery(Guid RunId) : IRequest<Result<IReadOnlyList<PayrollApprovalHistoryDto>>>;

public sealed class GetPayrollApprovalHistoryQueryHandler
    : IRequestHandler<GetPayrollApprovalHistoryQuery, Result<IReadOnlyList<PayrollApprovalHistoryDto>>>
{
    private readonly IPayrollApprovalService _service;
    public GetPayrollApprovalHistoryQueryHandler(IPayrollApprovalService service) => _service = service;

    public Task<Result<IReadOnlyList<PayrollApprovalHistoryDto>>> Handle(GetPayrollApprovalHistoryQuery request, CancellationToken cancellationToken)
        => _service.GetHistoryAsync(request.RunId, cancellationToken);
}

/// <summary>AC-4/FR-2 (BUG-076): read the tenant's configured payroll-approval step → role bindings.</summary>
public sealed record GetPayrollApprovalStepConfigQuery() : IRequest<Result<IReadOnlyList<PayrollApprovalStepConfigDto>>>;

public sealed class GetPayrollApprovalStepConfigQueryHandler
    : IRequestHandler<GetPayrollApprovalStepConfigQuery, Result<IReadOnlyList<PayrollApprovalStepConfigDto>>>
{
    private readonly IPayrollApprovalService _service;
    public GetPayrollApprovalStepConfigQueryHandler(IPayrollApprovalService service) => _service = service;

    public Task<Result<IReadOnlyList<PayrollApprovalStepConfigDto>>> Handle(GetPayrollApprovalStepConfigQuery request, CancellationToken cancellationToken)
        => _service.GetApprovalStepConfigAsync(cancellationToken);
}

/// <summary>FR-4: the approval-review summary (totals + previous-month variance + exceptions).</summary>
public sealed record GetPayrollApprovalSummaryQuery(Guid RunId) : IRequest<Result<PayrollApprovalSummaryDto>>;

public sealed class GetPayrollApprovalSummaryQueryHandler
    : IRequestHandler<GetPayrollApprovalSummaryQuery, Result<PayrollApprovalSummaryDto>>
{
    private readonly IPayrollApprovalService _service;
    public GetPayrollApprovalSummaryQueryHandler(IPayrollApprovalService service) => _service = service;

    public Task<Result<PayrollApprovalSummaryDto>> Handle(GetPayrollApprovalSummaryQuery request, CancellationToken cancellationToken)
        => _service.GetSummaryAsync(request.RunId, cancellationToken);
}
