namespace HRM.Domain.Payroll;

/// <summary>
/// One progressive tax band for the pure calculator (BR-3). Money/bounds are decimal; rate is a percentage
/// 0..100. <see cref="To"/> null = unlimited (the top band).
/// </summary>
/// <param name="From">Inclusive lower bound of the band.</param>
/// <param name="To">Exclusive upper bound; null = unlimited.</param>
/// <param name="RatePercentage">Rate applied to income within this band (0..100).</param>
public readonly record struct TaxBand(decimal From, decimal? To, decimal RatePercentage);

/// <summary>
/// Inputs for a social-security (EPF/ETF/etc.) contribution computation (AC-2/BR-8). The contribution base
/// is supplied already resolved (BASIC, gross, or a custom sum) by the caller; the calculator only applies
/// the ceiling and rates so it stays pure and data-driven.
/// </summary>
/// <param name="Base">The resolved wage base for the period (e.g. monthly basic).</param>
/// <param name="EmployeeRatePercentage">Employee contribution rate (0..100).</param>
/// <param name="EmployerRatePercentage">Employer contribution rate (0..100).</param>
/// <param name="WageCeiling">Per-period wage ceiling; null = no ceiling (BR-8).</param>
public readonly record struct SocialSecurityInput(
    decimal Base,
    decimal EmployeeRatePercentage,
    decimal EmployerRatePercentage,
    decimal? WageCeiling);

/// <summary>Computed employee + employer social-security contributions for a period.</summary>
public readonly record struct SocialSecurityResult(decimal EmployeeContribution, decimal EmployerContribution);

/// <summary>
/// Pure, deterministic statutory-deduction calculator (US-PAY-006 NFR-5). No DB / tenant / clock / config
/// access, so the financially/legally sensitive math is fully unit-testable with golden datasets (NFR-3
/// &gt;= 90%). Mirrors the design of <see cref="SalaryFormula"/> / <see cref="PayrollSlipCalculator"/>:
/// the caller resolves the rule version + bases, this class does only the arithmetic.
///
/// <list type="bullet">
///   <item><b>Progressive income tax</b> over slabs — each slab's rate applies ONLY to the income within
///   its [from, to) band (BR-3).</item>
///   <item><b>Social security with a wage ceiling</b> — contribution base is capped at the ceiling before
///   applying the rate (AC-2/BR-8).</item>
///   <item><b>Taxable income</b> = gross − exempt − declared exemptions, floored at zero (BR-2); a
///   below-threshold employee skips tax entirely (BR-6).</item>
/// </list>
/// All money is rounded half-up to 2 dp (numeric(18,2)).
/// </summary>
public static class StatutoryCalculator
{
    /// <summary>
    /// BR-2: taxable income = gross earnings − exempt earnings − declared exemptions, never below zero.
    /// </summary>
    public static decimal ComputeTaxableIncome(decimal grossEarnings, decimal exemptEarnings, decimal declaredExemptions)
    {
        var taxable = grossEarnings - exemptEarnings - declaredExemptions;
        return taxable < 0m ? 0m : Round(taxable);
    }

    /// <summary>
    /// Progressive income tax over the supplied bands (BR-3). Each band taxes only the portion of
    /// <paramref name="taxableIncome"/> that falls within [from, to). Bands are sorted by their lower bound
    /// defensively. Negative income yields zero.
    ///
    /// <para>Golden (US-PAY-006 §11): income 750,000 over [0–250K@0%, 250K–500K@5%, 500K–1M@20%]
    /// ⇒ 0 + 12,500 + 50,000 = 62,500.</para>
    /// </summary>
    public static decimal ComputeProgressiveTax(decimal taxableIncome, IReadOnlyList<TaxBand> bands)
    {
        if (taxableIncome <= 0m || bands.Count == 0)
            return 0m;

        decimal tax = 0m;
        foreach (var band in bands.OrderBy(b => b.From))
        {
            if (taxableIncome <= band.From)
                break; // income does not reach this band (bands are ascending).

            var upper = band.To ?? taxableIncome;
            // Portion of income that falls within this band's [from, upper) range.
            var amountInBand = Math.Min(taxableIncome, upper) - band.From;
            if (amountInBand <= 0m)
                continue;

            tax += amountInBand * (band.RatePercentage / 100m);
        }

        return Round(tax);
    }

    /// <summary>
    /// Income tax with the BR-6 below-threshold skip baked in: if the (taxable) income is at or below
    /// <paramref name="taxExemptThreshold"/>, tax is zero regardless of the slabs (a tax-exempt employee).
    /// Otherwise the progressive computation applies to the full taxable income.
    /// </summary>
    public static decimal ComputeIncomeTax(
        decimal taxableIncome, IReadOnlyList<TaxBand> bands, decimal taxExemptThreshold = 0m)
    {
        if (taxableIncome <= taxExemptThreshold)
            return 0m;
        return ComputeProgressiveTax(taxableIncome, bands);
    }

    /// <summary>
    /// Social-security contribution with a wage ceiling (AC-2/BR-8):
    /// <c>employee = min(base, ceiling) * employee_rate</c>, <c>employer = min(base, ceiling) * employer_rate</c>.
    /// A null/zero ceiling means no cap. Negative base yields zero contributions.
    ///
    /// <para>Golden (US-PAY-006 §11): base 20,000, ceiling 15,000, rate 12% ⇒ 1,800 (not 2,400).</para>
    /// </summary>
    public static SocialSecurityResult ComputeSocialSecurity(SocialSecurityInput input)
    {
        var @base = input.Base < 0m ? 0m : input.Base;
        var cappedBase = input.WageCeiling is { } ceiling && ceiling > 0m
            ? Math.Min(@base, ceiling)
            : @base;

        var employee = Round(cappedBase * (input.EmployeeRatePercentage / 100m));
        var employer = Round(cappedBase * (input.EmployerRatePercentage / 100m));
        return new SocialSecurityResult(employee, employer);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
