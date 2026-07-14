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
public sealed record StatutoryWageInput(
    decimal MonthlyGross,
    decimal MonthlyBasic,
    decimal ExemptEarnings,
    decimal DeclaredExemptions,
    IReadOnlyDictionary<Guid, decimal>? ComponentAmountsById);

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
}
