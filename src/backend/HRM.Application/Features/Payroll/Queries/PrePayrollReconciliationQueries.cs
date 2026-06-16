using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Queries;

/// <summary>
/// The pre-payroll attendance reconciliation report for a period (US-PAY-010 FR-7): per-employee working/
/// present/absent days, approved-leave days by type, overtime hours, and calculated LOP days.
/// </summary>
public sealed record GetPrePayrollReconciliationQuery(int PayYear, int PayMonth)
    : IRequest<Result<PrePayrollReconciliationDto>>;

public sealed class GetPrePayrollReconciliationQueryHandler
    : IRequestHandler<GetPrePayrollReconciliationQuery, Result<PrePayrollReconciliationDto>>
{
    private readonly IPayrollRunService _service;
    public GetPrePayrollReconciliationQueryHandler(IPayrollRunService service) => _service = service;

    public Task<Result<PrePayrollReconciliationDto>> Handle(GetPrePayrollReconciliationQuery request, CancellationToken cancellationToken)
        => _service.GetPrePayrollReconciliationAsync(request.PayYear, request.PayMonth, cancellationToken);
}
