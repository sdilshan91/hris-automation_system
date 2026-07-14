using HRM.Application.Common.Models;
using HRM.Application.DTOs;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Enums;

namespace HRM.Application.Common.Interfaces;

/// <summary>Input for one tax slab when creating/updating a statutory rule (US-PAY-006 FR-1).</summary>
public sealed record TaxSlabInput(decimal SlabFrom, decimal? SlabTo, decimal RatePercentage, int OrderIndex);

/// <summary>Input for the social-security parameters when creating/updating a statutory rule (US-PAY-006 FR-2).</summary>
public sealed record SocialSecurityInputDto(
    decimal EmployeeRate,
    decimal EmployerRate,
    decimal? WageCeilingAnnual,
    StatutoryApplicableOn ApplicableOn,
    IReadOnlyList<Guid>? ApplicableComponentIds);

/// <summary>Input for creating a statutory rule (US-PAY-006 FR-1/FR-2/FR-3).</summary>
public sealed record CreateStatutoryRuleInput(
    StatutoryRuleType RuleType,
    string RuleName,
    string CountryCode,
    string FiscalYear,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    IReadOnlyList<TaxSlabInput> TaxSlabs,
    SocialSecurityInputDto? SocialSecurity);

/// <summary>Input for updating a statutory rule (US-PAY-006 FR-1/FR-2). The rule type itself is immutable.</summary>
public sealed record UpdateStatutoryRuleInput(
    string RuleName,
    string CountryCode,
    string FiscalYear,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    IReadOnlyList<TaxSlabInput> TaxSlabs,
    SocialSecurityInputDto? SocialSecurity);

/// <summary>Input for the FR-5 test calculation (no persistence). <c>CountryCode</c> selects which country's
/// statutory regime the preview computes under (multi-country tax foundation); when null the preview resolves
/// nothing (mirrors the run's fail-closed null-country behaviour).</summary>
public sealed record TestCalculationInput(
    decimal MonthlyGross,
    decimal? MonthlyBasic,
    decimal DeclaredMonthlyExemptions,
    decimal ExemptEarnings,
    string? FiscalYear,
    string? CountryCode);

/// <summary>
/// Statutory-rule configuration + calculation service (US-PAY-006). All operations are tenant-scoped via
/// ITenantContext + the EF global query filter (AC-4 cross-tenant isolation). CRUD is fiscal-year-versioned
/// with effective date ranges (FR-1/FR-4); clone-to-new-fiscal-year is supported (AC-3); the test
/// calculation (FR-5) is side-effect-free and delegates the math to the pure
/// <c>HRM.Domain.Payroll.StatutoryCalculator</c> (NFR-5). Slab contiguity (FR-6) is validated in the
/// FluentValidation layer.
/// </summary>
public interface IStatutoryRuleService
{
    Task<Result<StatutoryRuleDto>> CreateAsync(CreateStatutoryRuleInput input, CancellationToken cancellationToken = default);

    Task<Result<StatutoryRuleDto>> UpdateAsync(Guid ruleId, UpdateStatutoryRuleInput input, CancellationToken cancellationToken = default);

    Task<Result<StatutoryRuleDto>> GetByIdAsync(Guid ruleId, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<StatutoryRuleListItemDto>>> ListAsync(
        StatutoryRuleType? ruleType, string? fiscalYear, bool? isActive, int page, int pageSize,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid ruleId, CancellationToken cancellationToken = default);

    /// <summary>FR-4: distinct fiscal years that have statutory rules, newest-first (drives the FY selector).</summary>
    Task<Result<IReadOnlyList<string>>> ListFiscalYearsAsync(CancellationToken cancellationToken = default);

    /// <summary>AC-3: clones every rule (slabs + social-security) of one fiscal year into a new fiscal year.</summary>
    Task<Result<IReadOnlyList<StatutoryRuleDto>>> CloneFiscalYearAsync(
        string sourceFiscalYear, string targetFiscalYear, DateOnly effectiveFrom, DateOnly? effectiveTo,
        CancellationToken cancellationToken = default);

    /// <summary>FR-5: computes statutory deductions for a sample salary WITHOUT persisting.</summary>
    Task<Result<StatutoryCalculationResultDto>> TestCalculationAsync(TestCalculationInput input, CancellationToken cancellationToken = default);
}
