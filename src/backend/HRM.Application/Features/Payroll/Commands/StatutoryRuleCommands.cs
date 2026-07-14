using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Payroll.Commands;

/// <summary>Creates a statutory rule with its slabs / social-security parameters (US-PAY-006 FR-1/FR-2/FR-3).</summary>
public sealed record CreateStatutoryRuleCommand(
    StatutoryRuleType RuleType,
    string RuleName,
    string CountryCode,
    string FiscalYear,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    IReadOnlyList<TaxSlabInput> TaxSlabs,
    SocialSecurityInputDto? SocialSecurity,
    IReadOnlyList<ExemptionInput>? Exemptions = null,
    bool IsCumulative = false
) : IRequest<Result<StatutoryRuleDto>>;

public sealed class CreateStatutoryRuleCommandHandler : IRequestHandler<CreateStatutoryRuleCommand, Result<StatutoryRuleDto>>
{
    private readonly IStatutoryRuleService _service;
    public CreateStatutoryRuleCommandHandler(IStatutoryRuleService service) => _service = service;

    public Task<Result<StatutoryRuleDto>> Handle(CreateStatutoryRuleCommand request, CancellationToken cancellationToken)
        => _service.CreateAsync(new CreateStatutoryRuleInput(
            request.RuleType, request.RuleName, request.CountryCode, request.FiscalYear,
            request.EffectiveFrom, request.EffectiveTo, request.IsActive, request.TaxSlabs, request.SocialSecurity,
            request.Exemptions, request.IsCumulative),
            cancellationToken);
}

/// <summary>Updates a statutory rule (US-PAY-006 FR-1/FR-2). The rule type is immutable.</summary>
public sealed record UpdateStatutoryRuleCommand(
    Guid RuleId,
    string RuleName,
    string CountryCode,
    string FiscalYear,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    IReadOnlyList<TaxSlabInput> TaxSlabs,
    SocialSecurityInputDto? SocialSecurity,
    IReadOnlyList<ExemptionInput>? Exemptions = null,
    bool IsCumulative = false
) : IRequest<Result<StatutoryRuleDto>>;

public sealed class UpdateStatutoryRuleCommandHandler : IRequestHandler<UpdateStatutoryRuleCommand, Result<StatutoryRuleDto>>
{
    private readonly IStatutoryRuleService _service;
    public UpdateStatutoryRuleCommandHandler(IStatutoryRuleService service) => _service = service;

    public Task<Result<StatutoryRuleDto>> Handle(UpdateStatutoryRuleCommand request, CancellationToken cancellationToken)
        => _service.UpdateAsync(request.RuleId, new UpdateStatutoryRuleInput(
            request.RuleName, request.CountryCode, request.FiscalYear, request.EffectiveFrom,
            request.EffectiveTo, request.IsActive, request.TaxSlabs, request.SocialSecurity,
            request.Exemptions, request.IsCumulative),
            cancellationToken);
}

/// <summary>Soft-deletes a statutory rule (US-PAY-006).</summary>
public sealed record DeleteStatutoryRuleCommand(Guid RuleId) : IRequest<Result>;

public sealed class DeleteStatutoryRuleCommandHandler : IRequestHandler<DeleteStatutoryRuleCommand, Result>
{
    private readonly IStatutoryRuleService _service;
    public DeleteStatutoryRuleCommandHandler(IStatutoryRuleService service) => _service = service;

    public Task<Result> Handle(DeleteStatutoryRuleCommand request, CancellationToken cancellationToken)
        => _service.DeleteAsync(request.RuleId, cancellationToken);
}

/// <summary>Clones one fiscal year's statutory rules into a new fiscal year (US-PAY-006 AC-3).</summary>
public sealed record CloneFiscalYearCommand(
    string SourceFiscalYear,
    string TargetFiscalYear,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo
) : IRequest<Result<IReadOnlyList<StatutoryRuleDto>>>;

public sealed class CloneFiscalYearCommandHandler : IRequestHandler<CloneFiscalYearCommand, Result<IReadOnlyList<StatutoryRuleDto>>>
{
    private readonly IStatutoryRuleService _service;
    public CloneFiscalYearCommandHandler(IStatutoryRuleService service) => _service = service;

    public Task<Result<IReadOnlyList<StatutoryRuleDto>>> Handle(CloneFiscalYearCommand request, CancellationToken cancellationToken)
        => _service.CloneFiscalYearAsync(
            request.SourceFiscalYear, request.TargetFiscalYear, request.EffectiveFrom, request.EffectiveTo, cancellationToken);
}
