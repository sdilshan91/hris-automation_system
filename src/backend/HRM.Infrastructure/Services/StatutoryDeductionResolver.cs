using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Resolves the statutory rule versions in effect for a payroll period and computes the per-employee
/// deductions (US-PAY-006 FR-4). The single source of truth shared by the FR-5 test-calculation query and
/// the US-PAY-003 payroll-run engine, so the previewed numbers == the run numbers. All reads are
/// tenant-scoped via the EF global query filter (AC-4); all arithmetic is delegated to the pure
/// <see cref="StatutoryCalculator"/> (NFR-5). Per-employee resolution targets &lt; 10 ms (NFR-2); a Redis
/// cache of the resolved rule set (NFR-1) is the documented deferral — see the module note.
/// </summary>
public sealed class StatutoryDeductionResolver : IStatutoryDeductionResolver
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<StatutoryDeductionResolver> _logger;

    public StatutoryDeductionResolver(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ILogger<StatutoryDeductionResolver> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<StatutoryDeductions>> ResolveAsync(
        int payYear, int payMonth, StatutoryWageInput wage, string? fiscalYearOverride = null,
        string? countryCode = null, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<StatutoryDeductions>.Failure("Tenant context is not resolved.", 400);

        // Multi-country tax foundation (money-critical): with no resolved tax country we must resolve NOTHING —
        // NEVER apply an arbitrary country's rules. The payroll run skips + flags null-country employees; the
        // FR-5 preview passes the selected country. Return an empty (no-deduction) result cleanly, do not throw.
        if (string.IsNullOrWhiteSpace(countryCode))
            return Result<StatutoryDeductions>.Success(Empty(fiscalYearOverride));

        var country = countryCode.Trim().ToUpperInvariant();

        var periodDate = FiscalYearResolver.PeriodDate(payYear, payMonth);

        // Candidate rules: active, in this employee's tax COUNTRY, in effect for the period (and matching the FY
        // override when given). Filtering by country BEFORE the per-type effective selection is what stops two
        // countries' rules of the same type (e.g. IncomeTax) from colliding (latest EffectiveFrom winning arbitrarily).
        // TAX-3 guard: normalize the STORED CountryCode in the comparison (not just the incoming side). The write
        // path always trims+uppers on save, but a raw/seed/import write could bypass the service and store e.g.
        // "lk" or "LK "; without this the cumulative pre-scan (which groups the stored code with Trim()+ToUpper())
        // would thread prior-YTD while this resolver silently matched nothing → divergence on a money path. Must
        // mirror the pre-scan/report normalization EXACTLY — case AND whitespace — so btrim+upper (not upper alone).
        // `upper(btrim(country_code))` is non-sargable, but the per-tenant statutory rule set is tiny and this
        // defends already-stored dirty rows a DB CHECK could not. Mirrors PayrollRunProcessor/PayrollReportService.
        var query = _dbContext.StatutoryRules.AsNoTracking()
            .Include(r => r.TaxSlabs)
            .Include(r => r.Exemptions)
            .Include(r => r.SocialSecurityRule)
            .Where(r => r.IsActive && r.CountryCode!.Trim().ToUpper() == country);

        if (!string.IsNullOrWhiteSpace(fiscalYearOverride))
        {
            var fy = fiscalYearOverride.Trim();
            query = query.Where(r => r.FiscalYear == fy);
        }

        var allRules = await query.ToListAsync(cancellationToken);

        var effectiveRules = SelectEffectiveByType(periodDate, allRules);
        return Result<StatutoryDeductions>.Success(ComputeFor(effectiveRules, wage, fiscalYearOverride));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyDictionary<Guid, StatutoryDeductions>>> ResolveManyAsync(
        int payYear, int payMonth, IReadOnlyCollection<StatutoryWageBatchItem> items,
        string? fiscalYearOverride = null, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyDictionary<Guid, StatutoryDeductions>>.Failure(
                "Tenant context is not resolved.", 400);

        var results = new Dictionary<Guid, StatutoryDeductions>();
        if (items.Count == 0)
            return Result<IReadOnlyDictionary<Guid, StatutoryDeductions>>.Success(results);

        var periodDate = FiscalYearResolver.PeriodDate(payYear, payMonth);

        // ISSUE-197: ONE rule query per DISTINCT country, not per subject. Country normalization must mirror
        // ResolveAsync's exactly (Trim + ToUpperInvariant on the incoming side, Trim().ToUpper() on the stored
        // side) — a normalization that disagreed would silently resolve a different rule set for the same
        // employee depending on which entry point was used.
        foreach (var group in items.GroupBy(i => string.IsNullOrWhiteSpace(i.CountryCode)
                     ? null
                     : i.CountryCode!.Trim().ToUpperInvariant()))
        {
            // Null/blank country resolves NOTHING — identical to the single-employee contract. Never borrow
            // another country's rules onto a money path.
            if (group.Key is null)
            {
                foreach (var item in group)
                    results[item.Key] = Empty(fiscalYearOverride);
                continue;
            }

            var country = group.Key;
            var query = _dbContext.StatutoryRules.AsNoTracking()
                .Include(r => r.TaxSlabs)
                .Include(r => r.Exemptions)
                .Include(r => r.SocialSecurityRule)
                .Where(r => r.IsActive && r.CountryCode!.Trim().ToUpper() == country);

            if (!string.IsNullOrWhiteSpace(fiscalYearOverride))
            {
                var fy = fiscalYearOverride.Trim();
                query = query.Where(r => r.FiscalYear == fy);
            }

            var allRules = await query.ToListAsync(cancellationToken);
            var effectiveRules = SelectEffectiveByType(periodDate, allRules);

            // Selected ONCE for the whole country group, then reused — the rule set does not vary per employee.
            foreach (var item in group)
                results[item.Key] = ComputeFor(effectiveRules, item.Wage, fiscalYearOverride);
        }

        return Result<IReadOnlyDictionary<Guid, StatutoryDeductions>>.Success(results);
    }

    /// <summary>
    /// ISSUE-197: the ENTIRE per-employee computation, pure over (effective rules, wage). Extracted so the
    /// single-employee <see cref="ResolveAsync"/> and the batch <see cref="ResolveManyAsync"/> run the SAME
    /// arithmetic rather than two implementations that agree today and drift tomorrow — the failure class behind
    /// BUG-291, BUG-293 and DF-62-parity. Pinned by a cross-path agreement test; do not inline either caller.
    /// </summary>
    private static StatutoryDeductions ComputeFor(
        Dictionary<StatutoryRuleType, StatutoryRule> effectiveRules,
        StatutoryWageInput wage,
        string? fiscalYearOverride)
    {
        if (effectiveRules.Count == 0)
            return Empty(fiscalYearOverride);

        var lines = new List<StatutoryDeductionLine>();
        decimal incomeTax = 0m, employeeEpf = 0m, employerEpf = 0m, etf = 0m, professionalTax = 0m, other = 0m;

        // TAX-2: sum the configurable income-tax exemptions on the effective IncomeTax rule (each reduces the
        // taxable base — never EPF/ETF). Percentages of a component read the amount from ComponentAmountsById.
        var exemptionsTotal = 0m;
        if (effectiveRules.TryGetValue(StatutoryRuleType.IncomeTax, out var taxRule))
        {
            foreach (var e in taxRule.Exemptions)
            {
                decimal? componentAmount = null;
                if (e.CalculationType == ExemptionCalculationType.PercentOfComponent
                    && e.ComponentId is { } cid
                    && wage.ComponentAmountsById is not null
                    && wage.ComponentAmountsById.TryGetValue(cid, out var amt))
                    componentAmount = amt;

                exemptionsTotal += StatutoryCalculator.ComputeExemption(
                    e.CalculationType, e.Value, wage.MonthlyGross, componentAmount, e.MaxAmount, e.IsAnnual);
            }
        }

        // BR-2: taxable income = gross − exempt − declared exemptions. TAX-2: the configurable exemptions add to
        // the wage-level DeclaredExemptions so BOTH subtract from the tax base.
        var taxableIncome = StatutoryCalculator.ComputeTaxableIncome(
            wage.MonthlyGross, wage.ExemptEarnings, wage.DeclaredExemptions + exemptionsTotal);
        var fiscalYear = effectiveRules.Values.First().FiscalYear;

        foreach (var (type, rule) in effectiveRules)
        {
            switch (type)
            {
                case StatutoryRuleType.IncomeTax:
                {
                    // Bands come from the SAME resolved rule whose IsCumulative flag drives the branch below.
                    var bands = rule.TaxSlabs
                        .OrderBy(s => s.OrderIndex)
                        .Select(s => new TaxBand(s.SlabFrom, s.SlabTo, s.RatePercentage))
                        .ToList();
                    if (rule.IsCumulative)
                    {
                        // TAX-3: cumulative PAYE. The bands are ANNUAL thresholds; withhold the YTD true-up delta
                        // tax(prior-YTD-taxable + this-month-taxable) − tax-already-withheld-YTD. StatutoryDeductions
                        // still reports THIS month's taxable + this month's WITHHELD (the delta) for slip persistence.
                        var cumulativeTaxable = wage.PriorTaxableIncomeYtd + taxableIncome;
                        incomeTax = StatutoryCalculator.ComputeIncomeTaxYtd(
                            cumulativeTaxable, bands, wage.PriorTaxWithheldYtd);
                        if (incomeTax > 0m)
                            lines.Add(new StatutoryDeductionLine(
                                rule.Id, rule.RuleName, incomeTax, IsEmployerContribution: false,
                                $"cumulative PAYE on {cumulativeTaxable:0.##} YTD less {wage.PriorTaxWithheldYtd:0.##} withheld"));
                    }
                    else
                    {
                        incomeTax = StatutoryCalculator.ComputeIncomeTax(taxableIncome, bands);
                        if (incomeTax > 0m)
                            lines.Add(new StatutoryDeductionLine(
                                rule.Id, rule.RuleName, incomeTax, IsEmployerContribution: false,
                                $"progressive tax on {taxableIncome:0.##}"));
                    }
                    break;
                }
                case StatutoryRuleType.EPF when rule.SocialSecurityRule is { } ss:
                {
                    var result = ComputeSocial(ss, wage);
                    employeeEpf = result.EmployeeContribution;
                    employerEpf = result.EmployerContribution;
                    if (employeeEpf > 0m)
                        lines.Add(new StatutoryDeductionLine(rule.Id, $"{rule.RuleName} (Employee)", employeeEpf, false, BasisLabel(ss)));
                    if (employerEpf > 0m)
                        lines.Add(new StatutoryDeductionLine(rule.Id, $"{rule.RuleName} (Employer)", employerEpf, true, BasisLabel(ss)));
                    break;
                }
                case StatutoryRuleType.ETF when rule.SocialSecurityRule is { } ss:
                {
                    var result = ComputeSocial(ss, wage);
                    // ETF is an employer-side contribution (employee rate is typically 0).
                    etf = result.EmployerContribution > 0m ? result.EmployerContribution : result.EmployeeContribution;
                    if (etf > 0m)
                        lines.Add(new StatutoryDeductionLine(rule.Id, rule.RuleName, etf, IsEmployerContribution: true, BasisLabel(ss)));
                    break;
                }
                case StatutoryRuleType.ProfessionalTax when rule.SocialSecurityRule is { } ss:
                {
                    var result = ComputeSocial(ss, wage);
                    professionalTax = result.EmployeeContribution;
                    if (professionalTax > 0m)
                        lines.Add(new StatutoryDeductionLine(rule.Id, rule.RuleName, professionalTax, false, BasisLabel(ss)));
                    break;
                }
                case StatutoryRuleType.Custom when rule.SocialSecurityRule is { } ss:
                {
                    var result = ComputeSocial(ss, wage);
                    other += result.EmployeeContribution;
                    if (result.EmployeeContribution > 0m)
                        lines.Add(new StatutoryDeductionLine(rule.Id, rule.RuleName, result.EmployeeContribution, false, BasisLabel(ss)));
                    if (result.EmployerContribution > 0m)
                        lines.Add(new StatutoryDeductionLine(rule.Id, $"{rule.RuleName} (Employer)", result.EmployerContribution, true, BasisLabel(ss)));
                    break;
                }
            }
        }

        var employeeTotal = Round(incomeTax + employeeEpf + professionalTax + other);
        var employerTotal = Round(employerEpf + etf);

        return new StatutoryDeductions
        {
            TaxableIncome = taxableIncome,
            IncomeTax = incomeTax,
            EmployeeEpf = employeeEpf,
            EmployerEpf = employerEpf,
            Etf = etf,
            ProfessionalTax = professionalTax,
            OtherStatutory = other,
            ExemptionsApplied = Round(exemptionsTotal),
            TotalEmployeeDeductions = employeeTotal,
            TotalEmployerContributions = employerTotal,
            Lines = lines,
            FiscalYear = fiscalYear,
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>FR-4: for each rule type, pick the single version whose effective range contains the period date.</summary>
    private static Dictionary<StatutoryRuleType, StatutoryRule> SelectEffectiveByType(
        DateOnly periodDate, List<StatutoryRule> rules)
    {
        var result = new Dictionary<StatutoryRuleType, StatutoryRule>();
        foreach (var group in rules.GroupBy(r => r.RuleType))
        {
            var candidates = group.ToList();
            var ranges = candidates.Select(r => (r.EffectiveFrom, r.EffectiveTo)).ToList();
            var idx = FiscalYearResolver.SelectEffective(periodDate, ranges);
            if (idx >= 0)
                result[group.Key] = candidates[idx];
        }
        return result;
    }

    /// <summary>Resolves the contribution base named by the rule, capped per-period, then applies the rates (BR-8).</summary>
    private static SocialSecurityResult ComputeSocial(SocialSecurityRule ss, StatutoryWageInput wage)
    {
        var @base = ss.ApplicableOn switch
        {
            StatutoryApplicableOn.Gross => wage.MonthlyGross,
            StatutoryApplicableOn.Custom => ResolveCustomBase(ss, wage),
            _ => wage.MonthlyBasic, // Basic (default)
        };

        // BR-8: per-period (monthly) ceiling = annual / 12.
        decimal? monthlyCeiling = ss.WageCeilingAnnual is { } annual && annual > 0m ? annual / 12m : null;

        return StatutoryCalculator.ComputeSocialSecurity(new SocialSecurityInput(
            @base, ss.EmployeeRate, ss.EmployerRate, monthlyCeiling));
    }

    private static decimal ResolveCustomBase(SocialSecurityRule ss, StatutoryWageInput wage)
    {
        if (ss.ApplicableComponentIds is not { Count: > 0 } ids || wage.ComponentAmountsById is null)
            return wage.MonthlyBasic; // no component data available → fall back to basic.

        decimal sum = 0m;
        foreach (var id in ids)
            if (wage.ComponentAmountsById.TryGetValue(id, out var amt))
                sum += amt;
        return sum;
    }

    private static string BasisLabel(SocialSecurityRule ss)
    {
        var ceiling = ss.WageCeilingAnnual is { } a && a > 0m ? $", ceiling {a / 12m:0.##}/mo" : "";
        return $"{ss.ApplicableOn}{ceiling}";
    }

    private static StatutoryDeductions Empty(string? fiscalYear) => new()
    {
        FiscalYear = string.IsNullOrWhiteSpace(fiscalYear) ? string.Empty : fiscalYear.Trim(),
    };

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
