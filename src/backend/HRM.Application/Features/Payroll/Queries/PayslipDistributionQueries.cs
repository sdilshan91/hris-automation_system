using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Queries;

/// <summary>
/// The per-run payslip-email distribution summary (US-PAY-011 BR-6/FR-5): total/sent/failed/skipped/queued +
/// started/completed. Backs the §8 summary card the FE polls (SignalR live progress deferred). Tenant-scoped.
/// </summary>
public sealed record GetPayslipDistributionSummaryQuery(Guid RunId)
    : IRequest<Result<PayslipDistributionSummaryDto>>;

public sealed class GetPayslipDistributionSummaryQueryHandler
    : IRequestHandler<GetPayslipDistributionSummaryQuery, Result<PayslipDistributionSummaryDto>>
{
    private readonly IPayslipDistributionService _service;
    public GetPayslipDistributionSummaryQueryHandler(IPayslipDistributionService service) => _service = service;

    public Task<Result<PayslipDistributionSummaryDto>> Handle(GetPayslipDistributionSummaryQuery request, CancellationToken cancellationToken)
        => _service.GetSummaryAsync(request.RunId, cancellationToken);
}
