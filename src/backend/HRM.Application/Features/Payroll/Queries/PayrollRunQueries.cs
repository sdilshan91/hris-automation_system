using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Queries;

/// <summary>Lists payroll runs for the tenant, newest period first (US-PAY-003 FR-8).</summary>
public sealed record ListPayrollRunsQuery() : IRequest<Result<IReadOnlyList<PayrollRunDto>>>;

public sealed class ListPayrollRunsQueryHandler : IRequestHandler<ListPayrollRunsQuery, Result<IReadOnlyList<PayrollRunDto>>>
{
    private readonly IPayrollRunService _service;
    public ListPayrollRunsQueryHandler(IPayrollRunService service) => _service = service;

    public Task<Result<IReadOnlyList<PayrollRunDto>>> Handle(ListPayrollRunsQuery request, CancellationToken cancellationToken)
        => _service.ListAsync(cancellationToken);
}

/// <summary>Gets a single payroll run by id (US-PAY-003 FR-8).</summary>
public sealed record GetPayrollRunQuery(Guid RunId) : IRequest<Result<PayrollRunDto>>;

public sealed class GetPayrollRunQueryHandler : IRequestHandler<GetPayrollRunQuery, Result<PayrollRunDto>>
{
    private readonly IPayrollRunService _service;
    public GetPayrollRunQueryHandler(IPayrollRunService service) => _service = service;

    public Task<Result<PayrollRunDto>> Handle(GetPayrollRunQuery request, CancellationToken cancellationToken)
        => _service.GetAsync(request.RunId, cancellationToken);
}

/// <summary>Gets a run's summary totals + run log (US-PAY-003 FR-8).</summary>
public sealed record GetPayrollRunSummaryQuery(Guid RunId) : IRequest<Result<PayrollRunSummaryDto>>;

public sealed class GetPayrollRunSummaryQueryHandler : IRequestHandler<GetPayrollRunSummaryQuery, Result<PayrollRunSummaryDto>>
{
    private readonly IPayrollRunService _service;
    public GetPayrollRunSummaryQueryHandler(IPayrollRunService service) => _service = service;

    public Task<Result<PayrollRunSummaryDto>> Handle(GetPayrollRunSummaryQuery request, CancellationToken cancellationToken)
        => _service.GetSummaryAsync(request.RunId, cancellationToken);
}

/// <summary>Gets a run's processed/total progress (US-PAY-003 FR-6). The FE polls this while Processing.</summary>
public sealed record GetPayrollRunProgressQuery(Guid RunId) : IRequest<Result<PayrollRunProgressDto>>;

public sealed class GetPayrollRunProgressQueryHandler : IRequestHandler<GetPayrollRunProgressQuery, Result<PayrollRunProgressDto>>
{
    private readonly IPayrollRunService _service;
    public GetPayrollRunProgressQueryHandler(IPayrollRunService service) => _service = service;

    public Task<Result<PayrollRunProgressDto>> Handle(GetPayrollRunProgressQuery request, CancellationToken cancellationToken)
        => _service.GetProgressAsync(request.RunId, cancellationToken);
}
