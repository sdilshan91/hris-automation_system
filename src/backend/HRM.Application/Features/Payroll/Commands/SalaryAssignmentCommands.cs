using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Commands;

/// <summary>Assigns a salary structure to an employee with a CTC breakdown (US-PAY-002 AC-1/AC-3).</summary>
public sealed record AssignSalaryStructureCommand(
    Guid EmployeeId,
    Guid SalaryStructureId,
    DateOnly EffectiveFrom,
    decimal AnnualCtc,
    string? Reason,
    IReadOnlyList<SalaryOverrideInput> Overrides
) : IRequest<Result<CtcBreakdownDto>>;

public sealed class AssignSalaryStructureCommandHandler : IRequestHandler<AssignSalaryStructureCommand, Result<CtcBreakdownDto>>
{
    private readonly ISalaryAssignmentService _service;
    public AssignSalaryStructureCommandHandler(ISalaryAssignmentService service) => _service = service;

    public Task<Result<CtcBreakdownDto>> Handle(AssignSalaryStructureCommand request, CancellationToken cancellationToken)
        => _service.AssignAsync(new AssignSalaryStructureInput(
            request.EmployeeId, request.SalaryStructureId, request.EffectiveFrom,
            request.AnnualCtc, request.Reason, request.Overrides), cancellationToken);
}

/// <summary>Bulk-assigns one salary structure to many employees with individual CTCs (US-PAY-002 AC-4/FR-5).</summary>
public sealed record BulkAssignSalaryStructureCommand(
    Guid SalaryStructureId,
    DateOnly EffectiveFrom,
    string? Reason,
    IReadOnlyList<BulkAssignEntryInput> Employees
) : IRequest<Result<BulkAssignResultDto>>;

public sealed class BulkAssignSalaryStructureCommandHandler : IRequestHandler<BulkAssignSalaryStructureCommand, Result<BulkAssignResultDto>>
{
    private readonly ISalaryAssignmentService _service;
    public BulkAssignSalaryStructureCommandHandler(ISalaryAssignmentService service) => _service = service;

    public Task<Result<BulkAssignResultDto>> Handle(BulkAssignSalaryStructureCommand request, CancellationToken cancellationToken)
        => _service.BulkAssignAsync(new BulkAssignInput(
            request.SalaryStructureId, request.EffectiveFrom, request.Reason, request.Employees), cancellationToken);
}
