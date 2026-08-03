using HRM.Application.Common.Models;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// The resolved per-period statutory deductions for one employee (US-PAY-006 FR-4/FR-5). Produced by
/// <see cref="IStatutoryDeductionResolver"/> for both the FR-5 test calculation and the payroll-run engine
/// (US-PAY-003) so the previewed numbers == the run numbers. All amounts are per-period (monthly).
/// </summary>
public sealed record StatutoryDeductions
{
    public decimal TaxableIncome { get; init; }
    public decimal IncomeTax { get; init; }
    public decimal EmployeeEpf { get; init; }
    public decimal EmployerEpf { get; init; }
    public decimal Etf { get; init; }
    public decimal ProfessionalTax { get; init; }
    public decimal OtherStatutory { get; init; }

    /// <summary>TAX-2: the total configurable income-tax exemption applied this period (reduces the taxable base,
    /// NOT itself a deduction line). Exposed as a scalar so preview/reporting can read it; 0 when none apply.</summary>
    public decimal ExemptionsApplied { get; init; }

    /// <summary>Deductions that reduce the employee's net pay (income tax + employee EPF + professional + other).</summary>
    public decimal TotalEmployeeDeductions { get; init; }

    /// <summary>Employer-side statutory cost (employer EPF + ETF) — informational, not deducted.</summary>
    public decimal TotalEmployerContributions { get; init; }

    /// <summary>The individual labelled lines (income tax, EPF, ETF, …) that were applied.</summary>
    public IReadOnlyList<StatutoryDeductionLine> Lines { get; init; } = [];

    /// <summary>The fiscal year whose rules were resolved (FR-4); empty when no rules were configured.</summary>
    public string FiscalYear { get; init; } = string.Empty;
}

/// <summary>One labelled statutory deduction/contribution line (US-PAY-006).</summary>
public sealed record StatutoryDeductionLine(
    Guid RuleId,
    string Label,
    decimal Amount,
    bool IsEmployerContribution,
    string Basis);

/// <summary>The per-employee wage inputs needed to compute statutory deductions (US-PAY-006 BR-2/BR-8).</summary>
/// <remarks>
/// TAX-3: <paramref name="PriorTaxableIncomeYtd"/> / <paramref name="PriorTaxWithheldYtd"/> carry the
/// fiscal-year-to-date taxable income and tax already withheld (prior periods, same country FY). They are used
/// ONLY when the resolved IncomeTax rule is cumulative; a monthly (default) rule ignores them, so both default
/// to 0 and existing callers are unaffected.
/// </remarks>
public sealed record StatutoryWageInput(
    decimal MonthlyGross,
    decimal MonthlyBasic,
    decimal ExemptEarnings,
    decimal DeclaredExemptions,
    IReadOnlyDictionary<Guid, decimal>? ComponentAmountsById,
    decimal PriorTaxableIncomeYtd = 0m,
    decimal PriorTaxWithheldYtd = 0m);

/// <summary>
/// Resolves the statutory rule versions in effect for a payroll period and computes the per-employee
/// deductions (US-PAY-006 FR-4). Shared by the FR-5 test-calculation query and the US-PAY-003 payroll-run
/// engine so a single source of truth governs both. All reads are tenant-scoped via the EF global filter.
/// </summary>
public interface IStatutoryDeductionResolver
{
    /// <summary>
    /// FR-4: resolves the active statutory rules for the given fiscal year (and pay date, used to pick the
    /// effective version), then computes the employee's per-period deductions via the pure
    /// <c>StatutoryCalculator</c>. When no rules are configured the result has zero deductions and an empty
    /// fiscal year, so callers degrade gracefully (the payroll run falls back to as-is statutory components).
    ///
    /// <para>Multi-country tax foundation: <paramref name="countryCode"/> is the employee's tax country (their
    /// branch/location country, or the tenant default). When provided, ONLY that country's rules are
    /// candidates, so two countries' rules of the same type never collide. When null/blank the resolver
    /// resolves NOTHING (returns an empty result) — the payroll run skips + flags employees whose tax country
    /// cannot be resolved rather than applying the wrong country's tax.</para>
    /// </summary>
    Task<Result<StatutoryDeductions>> ResolveAsync(
        int payYear, int payMonth, StatutoryWageInput wage, string? fiscalYearOverride = null,
        string? countryCode = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// ISSUE-197: the BATCH form. Loads the statutory rule set ONCE PER DISTINCT COUNTRY and then computes every
    /// item against it, instead of re-querying <c>StatutoryRules</c> per employee.
    ///
    /// <para>This exists because <see cref="ResolveAsync"/> has no cache (the NFR-1 Redis cache is a documented
    /// deferral), so calling it in a loop over a tenant's employees is an N+1: a 5 000-employee report would fire
    /// 5 000 rule queries. Typical tenants operate in one country, so a batch is ONE query regardless of headcount.</para>
    ///
    /// <para>It shares the single-employee path's arithmetic exactly — both funnel through the same private
    /// computation — so the batched numbers and the per-employee numbers cannot diverge. A cross-path agreement
    /// test pins that; see <c>StatutoryResolverBatchAgreementTests</c>.</para>
    ///
    /// <para>Per-item country semantics are identical to <see cref="ResolveAsync"/>: an item with a null/blank
    /// <c>CountryCode</c> resolves NOTHING and yields an empty (zero-deduction) result rather than borrowing
    /// another country's rules.</para>
    /// </summary>
    /// <param name="items">The per-subject wage inputs, each carrying its own tax country and a caller-chosen key.</param>
    /// <returns>One entry per input key. Keys absent from the input are absent from the result.</returns>
    Task<Result<IReadOnlyDictionary<Guid, StatutoryDeductions>>> ResolveManyAsync(
        int payYear, int payMonth, IReadOnlyCollection<StatutoryWageBatchItem> items,
        string? fiscalYearOverride = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// One subject of a <see cref="IStatutoryDeductionResolver.ResolveManyAsync"/> call (ISSUE-197).
/// </summary>
/// <param name="Key">Caller-chosen identity (e.g. the employee id) used to key the returned results.</param>
/// <param name="Wage">The same per-period wage inputs the single-employee path takes.</param>
/// <param name="CountryCode">
/// This subject's tax country. Null/blank resolves nothing for that subject — never a fallback to another
/// country's rules, matching the single-employee contract on a money path.
/// </param>
public sealed record StatutoryWageBatchItem(Guid Key, StatutoryWageInput Wage, string? CountryCode);
