using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Payroll.Commands;

/// <summary>Creates a salary component (US-PAY-001 FR-1).</summary>
public sealed record CreateSalaryComponentCommand(
    string Name,
    string Code,
    SalaryComponentType Type,
    CalculationMethod CalculationMethod,
    decimal? DefaultValue,
    string? FormulaExpression,
    bool IsTaxable,
    bool IsStatutory,
    bool IsActive,
    int ProcessingOrder
) : IRequest<Result<SalaryComponentDto>>;

public sealed class CreateSalaryComponentCommandHandler : IRequestHandler<CreateSalaryComponentCommand, Result<SalaryComponentDto>>
{
    private readonly ISalaryComponentService _service;
    public CreateSalaryComponentCommandHandler(ISalaryComponentService service) => _service = service;

    public Task<Result<SalaryComponentDto>> Handle(CreateSalaryComponentCommand request, CancellationToken cancellationToken)
        => _service.CreateAsync(new SalaryComponentInput(
            request.Name, request.Code, request.Type, request.CalculationMethod, request.DefaultValue,
            request.FormulaExpression, request.IsTaxable, request.IsStatutory, request.IsActive, request.ProcessingOrder),
            cancellationToken);
}

/// <summary>Updates a salary component (US-PAY-001 FR-1/AC-2).</summary>
public sealed record UpdateSalaryComponentCommand(
    Guid ComponentId,
    string Name,
    string Code,
    SalaryComponentType Type,
    CalculationMethod CalculationMethod,
    decimal? DefaultValue,
    string? FormulaExpression,
    bool IsTaxable,
    bool IsStatutory,
    bool IsActive,
    int ProcessingOrder
) : IRequest<Result<SalaryComponentDto>>;

public sealed class UpdateSalaryComponentCommandHandler : IRequestHandler<UpdateSalaryComponentCommand, Result<SalaryComponentDto>>
{
    private readonly ISalaryComponentService _service;
    public UpdateSalaryComponentCommandHandler(ISalaryComponentService service) => _service = service;

    public Task<Result<SalaryComponentDto>> Handle(UpdateSalaryComponentCommand request, CancellationToken cancellationToken)
        => _service.UpdateAsync(request.ComponentId, new SalaryComponentInput(
            request.Name, request.Code, request.Type, request.CalculationMethod, request.DefaultValue,
            request.FormulaExpression, request.IsTaxable, request.IsStatutory, request.IsActive, request.ProcessingOrder),
            cancellationToken);
}

/// <summary>Soft-deletes a salary component; blocked when in use (US-PAY-001 AC-5).</summary>
public sealed record DeleteSalaryComponentCommand(Guid ComponentId) : IRequest<Result>;

public sealed class DeleteSalaryComponentCommandHandler : IRequestHandler<DeleteSalaryComponentCommand, Result>
{
    private readonly ISalaryComponentService _service;
    public DeleteSalaryComponentCommandHandler(ISalaryComponentService service) => _service = service;

    public Task<Result> Handle(DeleteSalaryComponentCommand request, CancellationToken cancellationToken)
        => _service.DeleteAsync(request.ComponentId, cancellationToken);
}
