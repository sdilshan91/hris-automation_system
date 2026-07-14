// ============================================================================
// TAX-3: the new columns persist on REAL Postgres (the fast InMemory gate masks the numeric/boolean column
// mapping + the migration). Applies migrations on postgres:17-alpine and asserts:
//   * statutory_rule.is_cumulative round-trips (true), and
//   * payroll_slip.taxable_income / income_tax_withheld round-trip (numeric(18,2)).
// The money math is covered by the InMemory arms (YtdCumulativeTaxIntegrationTests) + the pure unit tests.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class YtdCumulativeTaxPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync() => await _postgres.StartAsync();
    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "acme";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    private AppDbContext CreateContext(ITenantContext tc, ICurrentUser cu) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
            .Options, tc);

    private StatutoryRuleService BuildService(AppDbContext db, ITenantContext tc, ICurrentUser cu) =>
        new(db, tc, cu, Substitute.For<IStatutoryDeductionResolver>(),
            Substitute.For<IPayrollAuditLogger>(), NullLogger<StatutoryRuleService>.Instance);

    private (ITenantContext tc, ICurrentUser cu) Actors()
    {
        var tc = new MutableTenantContext { TenantId = _tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        return (tc, cu);
    }

    [Fact]
    public async Task CreateCumulativeRule_IsCumulativeColumn_RoundTrips_OnPostgres()
    {
        var (tc, cu) = Actors();
        await using var db = CreateContext(tc, cu);
        await db.Database.MigrateAsync();

        var input = new CreateStatutoryRuleInput(
            StatutoryRuleType.IncomeTax, "PAYE", "LK", "2026-2027",
            new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), true,
            new[] { new TaxSlabInput(0m, 3_000_000m, 0m, 0), new TaxSlabInput(3_000_000m, null, 6m, 1) },
            SocialSecurity: null, Exemptions: null, IsCumulative: true);

        var created = await BuildService(db, tc, cu).CreateAsync(input);
        created.IsSuccess.Should().BeTrue(created.Error);
        created.Value!.IsCumulative.Should().BeTrue();

        // Reload from a fresh context so we read what actually hit Postgres.
        await using var db2 = CreateContext(tc, cu);
        var reloaded = await db2.StatutoryRules.AsNoTracking().SingleAsync(r => r.Id == created.Value.Id);
        reloaded.IsCumulative.Should().BeTrue();
    }

    [Fact]
    public async Task PayrollSlip_TaxColumns_RoundTrip_OnPostgres()
    {
        var (tc, cu) = Actors();
        await using var db = CreateContext(tc, cu);
        await db.Database.MigrateAsync();

        var slipId = BaseEntity.NewUuidV7();
        db.PayrollSlips.Add(new PayrollSlip
        {
            Id = slipId, TenantId = _tenantId,
            PayrollRunId = BaseEntity.NewUuidV7(), EmployeeId = BaseEntity.NewUuidV7(),
            GrossEarnings = 1_000_000m, TotalDeductions = 60_000m, NetSalary = 940_000m,
            WorkingDays = 22m, PaidDays = 22m, LopDays = 0m, PayMonth = 7, PayYear = 2026,
            TaxableIncome = 1_000_000m, IncomeTaxWithheld = 60_000m, IsDeleted = false,
        });
        await db.SaveChangesAsync();

        await using var db2 = CreateContext(tc, cu);
        var reloaded = await db2.PayrollSlips.AsNoTracking().SingleAsync(s => s.Id == slipId);
        reloaded.TaxableIncome.Should().Be(1_000_000m);
        reloaded.IncomeTaxWithheld.Should().Be(60_000m);
    }

    // ── The prior-slip YTD lookup uses an ORDINAL window `(PayYear*12 + PayMonth)` so it survives the
    //    calendar-year boundary (LK FY spans Apr→Mar). InMemory evaluates that arithmetic client-side; this
    //    proves Npgsql TRANSLATES it to correct SQL. Current period = Feb 2027 (ordinal 2027*12+2). Prior slips
    //    at Dec 2026 + Jan 2027 must be selected; a Jan 2026 slip (>13 months back) must be excluded. ──
    [Fact]
    public async Task PriorSlipOrdinalWindowQuery_TranslatesAcrossYearBoundary_OnPostgres()
    {
        var (tc, cu) = Actors();
        await using var db = CreateContext(tc, cu);
        await db.Database.MigrateAsync();

        var emp = BaseEntity.NewUuidV7();
        async Task Add(int year, int month, decimal withheld)
        {
            db.PayrollSlips.Add(new PayrollSlip
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, PayrollRunId = BaseEntity.NewUuidV7(),
                EmployeeId = emp, PayYear = year, PayMonth = month,
                GrossEarnings = 0m, TotalDeductions = 0m, NetSalary = 0m,
                WorkingDays = 22m, PaidDays = 22m, LopDays = 0m,
                TaxableIncome = 0m, IncomeTaxWithheld = withheld, IsDeleted = false,
            });
            await db.SaveChangesAsync();
        }
        await Add(2025, 12, 11m);  // 14 months before Feb 2027 → outside the 13-month window → excluded.
        await Add(2026, 12, 22m);  // within window, previous calendar year → included.
        await Add(2027, 1, 33m);   // within window, current calendar year → included.

        const int currentOrdinal = 2027 * 12 + 2; // Feb 2027.
        var lowerOrdinal = currentOrdinal - 13;

        await using var db2 = CreateContext(tc, cu);
        var rows = await db2.PayrollSlips.AsNoTracking()
            .Where(s => s.EmployeeId == emp
                && (s.PayYear * 12 + s.PayMonth) < currentOrdinal
                && (s.PayYear * 12 + s.PayMonth) >= lowerOrdinal)
            .Select(s => s.IncomeTaxWithheld)
            .ToListAsync();

        rows.Should().BeEquivalentTo(new[] { 22m, 33m }); // Dec-2026 + Jan-2027 only; Jan-2026 excluded.
    }
}
