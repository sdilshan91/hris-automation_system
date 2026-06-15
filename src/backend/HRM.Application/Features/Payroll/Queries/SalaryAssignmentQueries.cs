using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Queries;

/// <summary>Previews a CTC breakdown for a structure + CTC + overrides without persisting (US-PAY-002 FR-3).</summary>
public sealed record PreviewCtcBreakdownQuery(
    Guid SalaryStructureId,
    decimal AnnualCtc,
    IReadOnlyList<SalaryOverrideInput> Overrides
) : IRequest<Result<CtcBreakdownDto>>;

public sealed class PreviewCtcBreakdownQueryHandler : IRequestHandler<PreviewCtcBreakdownQuery, Result<CtcBreakdownDto>>
{
    private readonly ISalaryAssignmentService _service;
    public PreviewCtcBreakdownQueryHandler(ISalaryAssignmentService service) => _service = service;

    public Task<Result<CtcBreakdownDto>> Handle(PreviewCtcBreakdownQuery request, CancellationToken cancellationToken)
        => _service.PreviewAsync(new PreviewCtcInput(request.SalaryStructureId, request.AnnualCtc, request.Overrides), cancellationToken);
}

/// <summary>Gets an employee's current compensation snapshot (US-PAY-002 FR-4 read side).</summary>
public sealed record GetEmployeeCompensationQuery(Guid EmployeeId) : IRequest<Result<EmployeeCompensationDto>>;

public sealed class GetEmployeeCompensationQueryHandler : IRequestHandler<GetEmployeeCompensationQuery, Result<EmployeeCompensationDto>>
{
    private readonly ISalaryAssignmentService _service;
    public GetEmployeeCompensationQueryHandler(ISalaryAssignmentService service) => _service = service;

    public Task<Result<EmployeeCompensationDto>> Handle(GetEmployeeCompensationQuery request, CancellationToken cancellationToken)
        => _service.GetCurrentCompensationAsync(request.EmployeeId, cancellationToken);
}

/// <summary>Gets an employee's salary-revision history, newest first (US-PAY-002 FR-4/BR-3).</summary>
public sealed record GetSalaryRevisionHistoryQuery(Guid EmployeeId) : IRequest<Result<IReadOnlyList<SalaryRevisionDto>>>;

public sealed class GetSalaryRevisionHistoryQueryHandler : IRequestHandler<GetSalaryRevisionHistoryQuery, Result<IReadOnlyList<SalaryRevisionDto>>>
{
    private readonly ISalaryAssignmentService _service;
    public GetSalaryRevisionHistoryQueryHandler(ISalaryAssignmentService service) => _service = service;

    public Task<Result<IReadOnlyList<SalaryRevisionDto>>> Handle(GetSalaryRevisionHistoryQuery request, CancellationToken cancellationToken)
        => _service.GetRevisionHistoryAsync(request.EmployeeId, cancellationToken);
}
