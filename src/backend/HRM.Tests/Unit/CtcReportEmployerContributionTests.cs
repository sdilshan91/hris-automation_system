// ============================================================================
// ISSUE-197 — the CTC report's "Employer Contributions" column must carry real numbers.
//
// The column read 0.00 for every row. The filed cause ("BR-6 not reflected") was wrong: the
// contributions WERE computed, but as a 1:1 proxy over the employee's Statutory-type SALARY
// COMPONENTS. Tenants whose statutory liability is rule-driven — the normal case — model no such
// components, so the proxy summed nothing and Annual CTC collapsed to Annual Gross. Meanwhile
// IStatutoryDeductionResolver already produced the true employer EPF/ETF legs and the report
// never asked for them.
//
// These arms pin the OUTCOME (the rendered column), not the plumbing:
//   - with statutory rules configured and NO statutory salary components, the column is > 0
//     (the exact scenario that produced 0.00);
//   - the figure equals the resolver's own TotalEmployerContributions x 12, so the report cannot
//     drift from the payroll engine;
//   - Annual CTC = Annual Gross + employer contributions, i.e. the column actually feeds the total.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class CtcReportEmployerContributionTests
{
    private const string Country = "LK";
    private const decimal MonthlyBasic = 100_000m;
    private const decimal MonthlyAllowance = 50_000m;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _employeeId = BaseEntity.NewUuidV7();
    private readonly ITenantContext _tenantContext;

    public CtcReportEmployerContributionTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
    }

    private const int Col_AnnualGross = 4;
    private const int Col_EmployerContrib = 5;
    private const int Col_AnnualCtc = 6;

    [Fact]
    public async Task EmployerContributions_AreNonZero_WhenStatutoryIsRuleDrivenNotComponentDriven()
    {
        await SeedAsync();

        var rows = await RunCtcAsync();

        rows.Should().ContainSingle();
        var employer = decimal.Parse(rows[0].Cells[Col_EmployerContrib]);

        employer.Should().BeGreaterThan(0m,
            "the tenant has EPF/ETF rules but no Statutory-type salary components — the exact case that "
            + "produced 0.00 before ISSUE-197");
    }

    [Fact]
    public async Task EmployerContributions_MatchTheResolverExactly_TimesTwelve()
    {
        await SeedAsync();

        var rows = await RunCtcAsync();
        var reported = decimal.Parse(rows[0].Cells[Col_EmployerContrib]);

        // The SAME resolver the payroll engine uses, asked directly.
        var direct = await Resolver().ResolveAsync(2026, 6,
            new StatutoryWageInput(
                MonthlyGross: MonthlyBasic + MonthlyAllowance,
                MonthlyBasic: MonthlyBasic,
                ExemptEarnings: 0m, DeclaredExemptions: 0m, ComponentAmountsById: null),
            null, Country);

        direct.IsSuccess.Should().BeTrue(direct.Error);
        var expected = decimal.Round(direct.Value!.TotalEmployerContributions * 12m, 2, MidpointRounding.AwayFromZero);

        expected.Should().BeGreaterThan(0m, "otherwise this arm asserts 0 == 0");
        reported.Should().Be(expected,
            "the report must not compute its own employer figure — it must be the engine's");
    }

    [Fact]
    public async Task AnnualCtc_IsGrossPlusEmployerContributions()
    {
        await SeedAsync();

        var rows = await RunCtcAsync();
        var gross = decimal.Parse(rows[0].Cells[Col_AnnualGross]);
        var employer = decimal.Parse(rows[0].Cells[Col_EmployerContrib]);
        var ctc = decimal.Parse(rows[0].Cells[Col_AnnualCtc]);

        ctc.Should().Be(gross + employer, "the column must actually feed the CTC total, not sit beside it");
        ctc.Should().BeGreaterThan(gross, "a CTC equal to gross is the ISSUE-197 symptom");
    }

    /// <summary>
    /// The employee's tax country cannot be resolved (no location country, no tenant default), so the resolver
    /// must contribute nothing rather than borrow another country's rates. Pins the money-path contract at the
    /// report boundary, where a silent fallback would be invisible.
    /// </summary>
    [Fact]
    public async Task UnresolvableTaxCountry_ReportsZero_RatherThanBorrowingRules()
    {
        await SeedAsync(tenantDefaultCountry: null);

        var rows = await RunCtcAsync();

        decimal.Parse(rows[0].Cells[Col_EmployerContrib]).Should().Be(0m,
            "no resolved tax country must mean no statutory cost, never another country's rates");
    }

    // ── harness ──────────────────────────────────────────────────────────

    private async Task<List<PayrollReportRow>> RunCtcAsync()
    {
        var db = CreateDb();
        var svc = new PayrollReportService(
            db, _tenantContext,
            Substitute.For<IPayrollAuditLogger>(),
            NullLogger<PayrollReportService>.Instance,
            new StatutoryDeductionResolver(db, _tenantContext, NullLogger<StatutoryDeductionResolver>.Instance));

        var result = await svc.GenerateReportAsync(
            PayrollReportType.Ctc, new PayrollReportQueryParams { PayMonth = 6, PayYear = 2026 });

        result.IsSuccess.Should().BeTrue(result.Error);
        return [.. result.Value!.Rows];
    }

    private StatutoryDeductionResolver Resolver() =>
        new(CreateDb(), _tenantContext, NullLogger<StatutoryDeductionResolver>.Instance);

    private AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, _tenantContext);

    private async Task SeedAsync(string? tenantDefaultCountry = Country)
    {
        using var db = CreateDb();
        var from = new DateOnly(2026, 1, 1);

        db.Tenants.Add(new Tenant
        {
            Id = _tenantId, Subdomain = "acme", Name = "Acme",
            DefaultCountryCode = tenantDefaultCountry,
        });

        var deptId = BaseEntity.NewUuidV7();
        var jobId = BaseEntity.NewUuidV7();
        db.Departments.Add(new Department { Id = deptId, TenantId = _tenantId, Name = "Ops", Code = "OPS", IsActive = true });
        db.JobTitles.Add(new JobTitle { Id = jobId, TenantId = _tenantId, TitleName = "Agent", IsActive = true });

        db.Employees.Add(new Employee
        {
            Id = _employeeId, TenantId = _tenantId, EmployeeNo = "EMP-1",
            FirstName = "Ada", LastName = "Lovelace", Email = "ada@acme.test",
            DateOfJoining = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DepartmentId = deptId, JobTitleId = jobId, EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active, IsActive = true,
        });

        // Earning components ONLY — deliberately NO Statutory-type components, which is what defeated the
        // old proxy and produced the 0.00 column.
        var basic = new SalaryComponent
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Code = "BASIC",
            Name = "Basic", Type = SalaryComponentType.Earning, IsActive = true,
        };
        var allowance = new SalaryComponent
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Code = "HRA",
            Name = "House Rent Allowance", Type = SalaryComponentType.Earning, IsActive = true,
        };
        db.SalaryComponents.AddRange(basic, allowance);

        db.EmployeeSalaryComponents.AddRange(
            new EmployeeSalaryComponent
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeId = _employeeId,
                SalaryComponentId = basic.Id, MonthlyAmount = MonthlyBasic, AnnualAmount = MonthlyBasic * 12m,
                EffectiveFrom = from,
            },
            new EmployeeSalaryComponent
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeId = _employeeId,
                SalaryComponentId = allowance.Id, MonthlyAmount = MonthlyAllowance, AnnualAmount = MonthlyAllowance * 12m,
                EffectiveFrom = from,
            });

        var epf = new StatutoryRule
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, RuleType = StatutoryRuleType.EPF,
            RuleName = "EPF", CountryCode = Country, FiscalYear = "2026", EffectiveFrom = from, IsActive = true,
        };
        epf.SocialSecurityRule = new SocialSecurityRule
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, StatutoryRuleId = epf.Id,
            EmployeeRate = 8m, EmployerRate = 12m, ApplicableOn = StatutoryApplicableOn.Basic,
        };

        var etf = new StatutoryRule
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, RuleType = StatutoryRuleType.ETF,
            RuleName = "ETF", CountryCode = Country, FiscalYear = "2026", EffectiveFrom = from, IsActive = true,
        };
        etf.SocialSecurityRule = new SocialSecurityRule
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, StatutoryRuleId = etf.Id,
            EmployeeRate = 0m, EmployerRate = 3m, ApplicableOn = StatutoryApplicableOn.Basic,
        };

        db.StatutoryRules.AddRange(epf, etf);
        await db.SaveChangesAsync();
    }
}
