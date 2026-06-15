using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Commands;

/// <summary>Creates a salary structure with its component links (US-PAY-001 FR-2/FR-3).</summary>
public sealed record CreateSalaryStructureCommand(
    string Name,
    string Code,
    string? Description,
    DateOnly EffectiveFrom,
    bool IsDefault,
    bool IsActive,
    IReadOnlyList<SalaryStructureComponentInput> Components
) : IRequest<Result<SalaryStructureDto>>;

public sealed class CreateSalaryStructureCommandHandler : IRequestHandler<CreateSalaryStructureCommand, Result<SalaryStructureDto>>
{
    private readonly ISalaryStructureService _service;
    public CreateSalaryStructureCommandHandler(ISalaryStructureService service) => _service = service;

    public Task<Result<SalaryStructureDto>> Handle(CreateSalaryStructureCommand request, CancellationToken cancellationToken)
        => _service.CreateAsync(new SalaryStructureInput(
            request.Name, request.Code, request.Description, request.EffectiveFrom,
            request.IsDefault, request.IsActive, request.Components), cancellationToken);
}

/// <summary>Updates a salary structure and replaces its component links (US-PAY-001 FR-2/FR-3).</summary>
public sealed record UpdateSalaryStructureCommand(
    Guid StructureId,
    string Name,
    string Code,
    string? Description,
    DateOnly EffectiveFrom,
    bool IsDefault,
    bool IsActive,
    IReadOnlyList<SalaryStructureComponentInput> Components
) : IRequest<Result<SalaryStructureDto>>;

public sealed class UpdateSalaryStructureCommandHandler : IRequestHandler<UpdateSalaryStructureCommand, Result<SalaryStructureDto>>
{
    private readonly ISalaryStructureService _service;
    public UpdateSalaryStructureCommandHandler(ISalaryStructureService service) => _service = service;

    public Task<Result<SalaryStructureDto>> Handle(UpdateSalaryStructureCommand request, CancellationToken cancellationToken)
        => _service.UpdateAsync(request.StructureId, new SalaryStructureInput(
            request.Name, request.Code, request.Description, request.EffectiveFrom,
            request.IsDefault, request.IsActive, request.Components), cancellationToken);
}

/// <summary>Soft-deletes a salary structure (US-PAY-001).</summary>
public sealed record DeleteSalaryStructureCommand(Guid StructureId) : IRequest<Result>;

public sealed class DeleteSalaryStructureCommandHandler : IRequestHandler<DeleteSalaryStructureCommand, Result>
{
    private readonly ISalaryStructureService _service;
    public DeleteSalaryStructureCommandHandler(ISalaryStructureService service) => _service = service;

    public Task<Result> Handle(DeleteSalaryStructureCommand request, CancellationToken cancellationToken)
        => _service.DeleteAsync(request.StructureId, cancellationToken);
}

/// <summary>Links a single component to a structure (US-PAY-001 FR-3).</summary>
public sealed record LinkComponentToStructureCommand(
    Guid StructureId,
    Guid SalaryComponentId,
    decimal? OverrideValue,
    string? OverrideFormula,
    int ProcessingOrder,
    bool IsMandatory
) : IRequest<Result<SalaryStructureDto>>;

public sealed class LinkComponentToStructureCommandHandler : IRequestHandler<LinkComponentToStructureCommand, Result<SalaryStructureDto>>
{
    private readonly ISalaryStructureService _service;
    public LinkComponentToStructureCommandHandler(ISalaryStructureService service) => _service = service;

    public Task<Result<SalaryStructureDto>> Handle(LinkComponentToStructureCommand request, CancellationToken cancellationToken)
        => _service.LinkComponentAsync(request.StructureId, new SalaryStructureComponentInput(
            request.SalaryComponentId, request.OverrideValue, request.OverrideFormula,
            request.ProcessingOrder, request.IsMandatory), cancellationToken);
}

/// <summary>Removes a component link from a structure (US-PAY-001 FR-3).</summary>
public sealed record UnlinkComponentFromStructureCommand(Guid StructureId, Guid StructureComponentId)
    : IRequest<Result<SalaryStructureDto>>;

public sealed class UnlinkComponentFromStructureCommandHandler : IRequestHandler<UnlinkComponentFromStructureCommand, Result<SalaryStructureDto>>
{
    private readonly ISalaryStructureService _service;
    public UnlinkComponentFromStructureCommandHandler(ISalaryStructureService service) => _service = service;

    public Task<Result<SalaryStructureDto>> Handle(UnlinkComponentFromStructureCommand request, CancellationToken cancellationToken)
        => _service.UnlinkComponentAsync(request.StructureId, request.StructureComponentId, cancellationToken);
}

/// <summary>Reorders component processing priority within a structure (US-PAY-001 AC-4).</summary>
public sealed record ReorderStructureComponentsCommand(
    Guid StructureId,
    IReadOnlyList<(Guid StructureComponentId, int ProcessingOrder)> Order
) : IRequest<Result<SalaryStructureDto>>;

public sealed class ReorderStructureComponentsCommandHandler : IRequestHandler<ReorderStructureComponentsCommand, Result<SalaryStructureDto>>
{
    private readonly ISalaryStructureService _service;
    public ReorderStructureComponentsCommandHandler(ISalaryStructureService service) => _service = service;

    public Task<Result<SalaryStructureDto>> Handle(ReorderStructureComponentsCommand request, CancellationToken cancellationToken)
        => _service.ReorderComponentsAsync(request.StructureId, request.Order, cancellationToken);
}

/// <summary>Clones a salary structure into a new one (US-PAY-001 FR-6).</summary>
public sealed record CloneSalaryStructureCommand(Guid StructureId, string NewName, string NewCode)
    : IRequest<Result<SalaryStructureDto>>;

public sealed class CloneSalaryStructureCommandHandler : IRequestHandler<CloneSalaryStructureCommand, Result<SalaryStructureDto>>
{
    private readonly ISalaryStructureService _service;
    public CloneSalaryStructureCommandHandler(ISalaryStructureService service) => _service = service;

    public Task<Result<SalaryStructureDto>> Handle(CloneSalaryStructureCommand request, CancellationToken cancellationToken)
        => _service.CloneAsync(request.StructureId, request.NewName, request.NewCode, cancellationToken);
}
