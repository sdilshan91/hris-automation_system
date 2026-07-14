using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.DTOs;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Payroll.Queries;

/// <summary>Gets a single statutory rule by id (tenant-scoped).</summary>
public sealed record GetStatutoryRuleByIdQuery(Guid RuleId) : IRequest<Result<StatutoryRuleDto>>;

public sealed class GetStatutoryRuleByIdQueryHandler : IRequestHandler<GetStatutoryRuleByIdQuery, Result<StatutoryRuleDto>>
{
    private readonly IStatutoryRuleService _service;
    public GetStatutoryRuleByIdQueryHandler(IStatutoryRuleService service) => _service = service;

    public Task<Result<StatutoryRuleDto>> Handle(GetStatutoryRuleByIdQuery request, CancellationToken cancellationToken)
        => _service.GetByIdAsync(request.RuleId, cancellationToken);
}

/// <summary>Lists statutory rules for the tenant, paged + filterable by type / fiscal year / active.</summary>
public sealed record ListStatutoryRulesQuery(
    StatutoryRuleType? RuleType,
    string? FiscalYear,
    bool? IsActive,
    int Page = 1,
    int PageSize = 25
) : IRequest<Result<PagedResult<StatutoryRuleListItemDto>>>;

public sealed class ListStatutoryRulesQueryHandler : IRequestHandler<ListStatutoryRulesQuery, Result<PagedResult<StatutoryRuleListItemDto>>>
{
    private readonly IStatutoryRuleService _service;
    public ListStatutoryRulesQueryHandler(IStatutoryRuleService service) => _service = service;

    public Task<Result<PagedResult<StatutoryRuleListItemDto>>> Handle(ListStatutoryRulesQuery request, CancellationToken cancellationToken)
        => _service.ListAsync(request.RuleType, request.FiscalYear, request.IsActive, request.Page, request.PageSize, cancellationToken);
}

/// <summary>FR-4: distinct fiscal years that have statutory rules, newest-first (for the FY selector).</summary>
public sealed record ListStatutoryFiscalYearsQuery() : IRequest<Result<IReadOnlyList<string>>>;

public sealed class ListStatutoryFiscalYearsQueryHandler : IRequestHandler<ListStatutoryFiscalYearsQuery, Result<IReadOnlyList<string>>>
{
    private readonly IStatutoryRuleService _service;
    public ListStatutoryFiscalYearsQueryHandler(IStatutoryRuleService service) => _service = service;

    public Task<Result<IReadOnlyList<string>>> Handle(ListStatutoryFiscalYearsQuery request, CancellationToken cancellationToken)
        => _service.ListFiscalYearsAsync(cancellationToken);
}

/// <summary>FR-5: computes statutory deductions for a sample salary WITHOUT persisting.</summary>
public sealed record TestStatutoryCalculationQuery(
    decimal MonthlyGross,
    decimal? MonthlyBasic,
    decimal DeclaredMonthlyExemptions,
    decimal ExemptEarnings,
    string? FiscalYear,
    string? CountryCode,
    IReadOnlyDictionary<Guid, decimal>? ComponentAmounts = null,
    decimal PriorTaxableIncomeYtd = 0m,
    decimal PriorTaxWithheldYtd = 0m
) : IRequest<Result<StatutoryCalculationResultDto>>;

public sealed class TestStatutoryCalculationQueryHandler : IRequestHandler<TestStatutoryCalculationQuery, Result<StatutoryCalculationResultDto>>
{
    private readonly IStatutoryRuleService _service;
    public TestStatutoryCalculationQueryHandler(IStatutoryRuleService service) => _service = service;

    public Task<Result<StatutoryCalculationResultDto>> Handle(TestStatutoryCalculationQuery request, CancellationToken cancellationToken)
        => _service.TestCalculationAsync(new TestCalculationInput(
            request.MonthlyGross, request.MonthlyBasic, request.DeclaredMonthlyExemptions,
            request.ExemptEarnings, request.FiscalYear, request.CountryCode,
            request.ComponentAmounts, request.PriorTaxableIncomeYtd, request.PriorTaxWithheldYtd), cancellationToken);
}
