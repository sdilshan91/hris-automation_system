// ============================================================================
// ISSUE-197 — the batch statutory resolver must agree with the single-employee one, EXACTLY.
//
// ResolveManyAsync exists so a per-employee report does not fire one StatutoryRules query per
// employee (the resolver has no cache — the NFR-1 Redis cache is a documented deferral). The
// danger in adding a second entry point to a money path is that the two agree today and drift
// later: that is precisely how BUG-291, BUG-293 and DF-62-parity happened.
//
// So the protection is NOT "both look right" — it is a CROSS-PATH AGREEMENT test. For the same
// inputs, every field of every result must be identical. Both paths funnel through the same
// private ComputeFor; if anyone re-inlines either one, these arms fail.
//
// Harness: EF InMemory. Deliberate — this asserts that TWO CODE PATHS agree, not that ledger
// arithmetic survives Postgres. Both paths issue the identical query shape and the identical
// arithmetic, so the provider cannot make one right and the other wrong. (The money-vs-Postgres
// arms live with the payroll run.)
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class StatutoryResolverBatchAgreementTests
{
    private const string Country = "LK";
    private const string Fy = "2026";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;

    public StatutoryResolverBatchAgreementTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
    }

    /// <summary>Wages chosen to span the slab boundaries and the EPF ceiling, not three variations of "typical".</summary>
    private static readonly StatutoryWageInput[] Wages =
    [
        new(MonthlyGross: 50_000m,    MonthlyBasic: 25_000m,  ExemptEarnings: 0m,     DeclaredExemptions: 0m,     ComponentAmountsById: null),
        new(MonthlyGross: 150_000m,   MonthlyBasic: 90_000m,  ExemptEarnings: 5_000m, DeclaredExemptions: 2_500m, ComponentAmountsById: null),
        new(MonthlyGross: 1_200_000m, MonthlyBasic: 800_000m, ExemptEarnings: 0m,     DeclaredExemptions: 0m,     ComponentAmountsById: null), // above the ceiling
        new(MonthlyGross: 0m,         MonthlyBasic: 0m,       ExemptEarnings: 0m,     DeclaredExemptions: 0m,     ComponentAmountsById: null), // zero-wage edge
    ];

    [Fact]
    public async Task Batch_AgreesWithSingle_FieldForField()
    {
        await SeedRulesAsync(Country);
        var svc = Resolver();

        var keys = Wages.Select(_ => Guid.NewGuid()).ToArray();

        var batch = await svc.ResolveManyAsync(2026, 6,
            [.. Wages.Select((w, i) => new StatutoryWageBatchItem(keys[i], w, Country))]);
        batch.IsSuccess.Should().BeTrue(batch.Error);

        for (var i = 0; i < Wages.Length; i++)
        {
            var single = await svc.ResolveAsync(2026, 6, Wages[i], null, Country);
            single.IsSuccess.Should().BeTrue(single.Error);

            var b = batch.Value![keys[i]];
            var s = single.Value!;

            // Field-for-field, not a spot check — a divergence in ANY component is a money divergence.
            b.TaxableIncome.Should().Be(s.TaxableIncome, $"wage[{i}] taxable income must match");
            b.IncomeTax.Should().Be(s.IncomeTax, $"wage[{i}] income tax must match");
            b.EmployeeEpf.Should().Be(s.EmployeeEpf, $"wage[{i}] employee EPF must match");
            b.EmployerEpf.Should().Be(s.EmployerEpf, $"wage[{i}] employer EPF must match");
            b.Etf.Should().Be(s.Etf, $"wage[{i}] ETF must match");
            b.ProfessionalTax.Should().Be(s.ProfessionalTax);
            b.OtherStatutory.Should().Be(s.OtherStatutory);
            b.ExemptionsApplied.Should().Be(s.ExemptionsApplied);
            b.TotalEmployeeDeductions.Should().Be(s.TotalEmployeeDeductions, $"wage[{i}] employee total must match");
            b.TotalEmployerContributions.Should().Be(s.TotalEmployerContributions,
                $"wage[{i}] EMPLOYER total must match — this is the figure the CTC report consumes");
            b.FiscalYear.Should().Be(s.FiscalYear);
            b.Lines.Select(l => (l.Label, l.Amount, l.IsEmployerContribution))
                .Should().BeEquivalentTo(s.Lines.Select(l => (l.Label, l.Amount, l.IsEmployerContribution)),
                    $"wage[{i}] the labelled lines must match, not merely the totals");
        }
    }

    /// <summary>
    /// The agreement above would still hold if BOTH paths returned zero for everything. This arm proves the
    /// fixture actually produces non-trivial numbers, so the comparison has something to compare.
    /// </summary>
    [Fact]
    public async Task Fixture_ProducesNonZeroEmployerContributions()
    {
        await SeedRulesAsync(Country);

        var r = await Resolver().ResolveAsync(2026, 6, Wages[1], null, Country);

        r.IsSuccess.Should().BeTrue(r.Error);
        r.Value!.TotalEmployerContributions.Should().BeGreaterThan(0m,
            "otherwise the agreement arm compares zero against zero and proves nothing");
        r.Value!.IncomeTax.Should().BeGreaterThan(0m);
    }

    /// <summary>
    /// Money-path contract: a subject with no resolved tax country must resolve NOTHING — never borrow the
    /// rules of a country that happens to be in the same batch. Mixed batch, so a naive "use the group's rules"
    /// implementation fails here.
    /// </summary>
    [Fact]
    public async Task NullCountryItem_ResolvesNothing_EvenAlongsideAResolvedCountry()
    {
        await SeedRulesAsync(Country);

        var withCountry = Guid.NewGuid();
        var without = Guid.NewGuid();

        var batch = await Resolver().ResolveManyAsync(2026, 6,
        [
            new StatutoryWageBatchItem(withCountry, Wages[1], Country),
            new StatutoryWageBatchItem(without, Wages[1], null),
        ]);

        batch.IsSuccess.Should().BeTrue(batch.Error);
        batch.Value![withCountry].TotalEmployerContributions.Should().BeGreaterThan(0m);

        var none = batch.Value![without];
        none.TotalEmployerContributions.Should().Be(0m, "a null tax country must never inherit LK's rules");
        none.IncomeTax.Should().Be(0m);
        none.FiscalYear.Should().BeEmpty();
    }

    /// <summary>Two countries in one batch must each get their OWN rule set, not whichever loaded last.</summary>
    [Fact]
    public async Task MultiCountryBatch_KeepsEachCountrysRulesSeparate()
    {
        await SeedRulesAsync(Country);              // LK: EPF employer 12%
        await SeedRulesAsync("SG", employerRate: 5m); // SG: employer 5%

        var lk = Guid.NewGuid();
        var sg = Guid.NewGuid();

        var batch = await Resolver().ResolveManyAsync(2026, 6,
        [
            new StatutoryWageBatchItem(lk, Wages[1], "LK"),
            new StatutoryWageBatchItem(sg, Wages[1], "SG"),
        ]);

        batch.IsSuccess.Should().BeTrue(batch.Error);
        batch.Value![lk].EmployerEpf.Should().BeGreaterThan(batch.Value![sg].EmployerEpf,
            "LK's 12% employer rate must not be applied to the SG subject");

        // And each still agrees with its own single-employee resolution.
        var lkSingle = await Resolver().ResolveAsync(2026, 6, Wages[1], null, "LK");
        var sgSingle = await Resolver().ResolveAsync(2026, 6, Wages[1], null, "SG");
        batch.Value![lk].TotalEmployerContributions.Should().Be(lkSingle.Value!.TotalEmployerContributions);
        batch.Value![sg].TotalEmployerContributions.Should().Be(sgSingle.Value!.TotalEmployerContributions);
    }

    [Fact]
    public async Task EmptyBatch_IsAnEmptyResult_NotAFailure()
    {
        await SeedRulesAsync(Country);
        var r = await Resolver().ResolveManyAsync(2026, 6, []);
        r.IsSuccess.Should().BeTrue();
        r.Value!.Should().BeEmpty();
    }

    // ── harness ──────────────────────────────────────────────────────────

    private StatutoryDeductionResolver Resolver() =>
        new(CreateDb(), _tenantContext, NullLogger<StatutoryDeductionResolver>.Instance);

    private AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, _tenantContext);

    private async Task SeedRulesAsync(string country, decimal employerRate = 12m)
    {
        using var db = CreateDb();
        var from = new DateOnly(2026, 1, 1);

        var tax = new StatutoryRule
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, RuleType = StatutoryRuleType.IncomeTax,
            RuleName = $"{country} PAYE", CountryCode = country, FiscalYear = Fy,
            EffectiveFrom = from, IsActive = true, IsCumulative = false,
        };
        tax.TaxSlabs.Add(new TaxSlab
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, StatutoryRuleId = tax.Id,
            SlabFrom = 0m, SlabTo = 100_000m, RatePercentage = 0m, OrderIndex = 0,
        });
        tax.TaxSlabs.Add(new TaxSlab
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, StatutoryRuleId = tax.Id,
            SlabFrom = 100_000m, SlabTo = 500_000m, RatePercentage = 6m, OrderIndex = 1,
        });
        tax.TaxSlabs.Add(new TaxSlab
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, StatutoryRuleId = tax.Id,
            SlabFrom = 500_000m, SlabTo = null, RatePercentage = 18m, OrderIndex = 2,
        });

        var epf = new StatutoryRule
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, RuleType = StatutoryRuleType.EPF,
            RuleName = $"{country} EPF", CountryCode = country, FiscalYear = Fy,
            EffectiveFrom = from, IsActive = true,
        };
        epf.SocialSecurityRule = new SocialSecurityRule
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, StatutoryRuleId = epf.Id,
            EmployeeRate = 8m, EmployerRate = employerRate,
            ApplicableOn = StatutoryApplicableOn.Basic, WageCeilingAnnual = 6_000_000m,
        };

        var etf = new StatutoryRule
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, RuleType = StatutoryRuleType.ETF,
            RuleName = $"{country} ETF", CountryCode = country, FiscalYear = Fy,
            EffectiveFrom = from, IsActive = true,
        };
        etf.SocialSecurityRule = new SocialSecurityRule
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, StatutoryRuleId = etf.Id,
            EmployeeRate = 0m, EmployerRate = 3m, ApplicableOn = StatutoryApplicableOn.Basic,
        };

        db.StatutoryRules.AddRange(tax, epf, etf);
        await db.SaveChangesAsync();
    }
}
