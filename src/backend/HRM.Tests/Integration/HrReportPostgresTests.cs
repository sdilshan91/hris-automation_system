// ============================================================================
// US-RPT-001: Pre-built HR reports — integration tests on REAL POSTGRES (E3, Leg-3 parity).
//
// Exercises HrReportService over a real AppDbContext with the ITenantContext-driven global
// query filter, covering:
//   - AC-5/FR-7: Tenant A's report excludes Tenant B's data (no cross-tenant leakage).
//   - AC-2: Headcount Summary total == count of the tenant's employees; active vs inactive per BR-4.
//   - AC-3/BR-3: Turnover rate = separations / avg-headcount * 100 (the 100-emp / terminate-10 -> 10% hint).
//   - FR-2: department filter restricts the population.
//
// PROVIDER: real PostgreSQL via Testcontainers (was EF InMemory until 2026-09-03, E3 slice 1).
// WHY: this service is almost entirely GROUP BY + ordering + decimal arithmetic, and EF InMemory is a
// LINQ-to-Objects provider — it translates no SQL, so grouping shape, ordering/collation, decimal
// rounding, unique indexes and FK enforcement all behave differently from Npgsql. Asserting report
// aggregation on InMemory proves the LINQ, not the query. Every assertion below is UNCHANGED from the
// InMemory suite this file replaces; only the provider and the (now FK-valid) seed rows differ.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Reports.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-RPT-001-PG")]
public sealed class HrReportPostgresTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _pg;

    // Fresh per TEST (xUnit builds a new test-class instance per method), so sibling tests are
    // isolated from each other by the tenant query filter even though they share the database.
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Dictionary<Guid, Guid> _jobTitles = [];

    public HrReportPostgresTests(PostgresContainerFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        using var db = Db(_tenantA);

        // Real FKs are enforced here (InMemory ignored them), so tenant + job-title rows must exist.
        foreach (var (tenantId, subdomain) in new[] { (_tenantA, $"a{_tenantA:N}"[..12]), (_tenantB, $"b{_tenantB:N}"[..12]) })
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, Subdomain = subdomain, Name = subdomain,
                DefaultCountryCode = "LK", FiscalYearStartMonth = 1,
            });
            var jobId = BaseEntity.NewUuidV7();
            _jobTitles[tenantId] = jobId;
            db.JobTitles.Add(new JobTitle
            {
                Id = jobId, TenantId = tenantId, TitleName = "Engineer", IsActive = true,
            });
        }
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "test";
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

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.TenantId.Returns(tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_pg.ConnectionString, n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(ctx), new AuditInterceptor(cu))
            .Options;
        return new AppDbContext(options, ctx);
    }

    private (AppDbContext Db, HrReportService Svc) Scope(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.TenantId.Returns(tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_pg.ConnectionString, n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(ctx), new AuditInterceptor(cu))
            .Options;
        var db = new AppDbContext(options, ctx);
        return (db, new HrReportService(db, ctx, NullLogger<HrReportService>.Instance));
    }

    // ── Seeding ─────────────────────────────────────────────────────────────

    private async Task<Guid> SeedDepartment(Guid tenantId, string name)
    {
        using var db = Db(tenantId);
        var id = BaseEntity.NewUuidV7();
        db.Departments.Add(new Department
        {
            Id = id, TenantId = tenantId, Name = name,
            Code = name[..Math.Min(3, name.Length)].ToUpperInvariant(),
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedEmployee(
        Guid tenantId, string no, Guid departmentId,
        EmployeeStatus status = EmployeeStatus.Active,
        EmploymentType type = EmploymentType.FullTime,
        DateTime? joining = null)
    {
        using var db = Db(tenantId);
        var id = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = id, TenantId = tenantId, EmployeeNo = no, FirstName = no, LastName = "X",
            Email = $"{no}@t.com", DateOfJoining = joining ?? new DateTime(2020, 1, 1),
            EmploymentType = type, Status = status, IsActive = status is EmployeeStatus.Active or EmployeeStatus.Probation,
            DepartmentId = departmentId, JobTitleId = _jobTitles[tenantId],
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SeedSeparation(Guid tenantId, Guid employeeId, DateTime effectiveDate, string newValue = "Terminated")
    {
        using var db = Db(tenantId);
        db.EmploymentHistories.Add(new EmploymentHistory
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = employeeId,
            ChangeType = "Status", PreviousValue = "Active", NewValue = newValue,
            EffectiveDate = effectiveDate, ChangedBy = "test",
        });
        await db.SaveChangesAsync();
    }

    // ── AC-5/FR-7: tenant isolation ─────────────────────────────────────────

    [Fact]
    public async Task HeadcountSummary_TenantA_ExcludesTenantB()
    {
        var deptA = await SeedDepartment(_tenantA, "Engineering");
        var deptB = await SeedDepartment(_tenantB, "Sales");
        await SeedEmployee(_tenantA, "A1", deptA);
        await SeedEmployee(_tenantA, "A2", deptA);
        await SeedEmployee(_tenantB, "B1", deptB);

        var (db, svc) = Scope(_tenantA);
        using (db)
        {
            var result = await svc.GenerateReportAsync(HrReportType.HeadcountSummary, new HrReportQueryParams());

            result.IsSuccess.Should().BeTrue();
            // Total headcount KPI == Tenant A's 2 employees; Tenant B's data is invisible via the global filter.
            var totalStat = result.Value!.Metadata.Summary.Single(s => s.Label == "Total Headcount");
            totalStat.Value.Should().Be(2);
        }
    }

    // ── AC-2: headcount happy path (active vs inactive per BR-4) ─────────────

    [Fact]
    public async Task HeadcountSummary_CountsActiveVsInactive_PerBr4()
    {
        var dept = await SeedDepartment(_tenantA, "Engineering");
        await SeedEmployee(_tenantA, "A1", dept, EmployeeStatus.Active);
        await SeedEmployee(_tenantA, "A2", dept, EmployeeStatus.Probation);   // active per BR-4
        await SeedEmployee(_tenantA, "A3", dept, EmployeeStatus.Terminated);  // separated
        await SeedEmployee(_tenantA, "A4", dept, EmployeeStatus.Suspended);   // not active

        var (db, svc) = Scope(_tenantA);
        using (db)
        {
            var result = await svc.GenerateReportAsync(HrReportType.HeadcountSummary, new HrReportQueryParams());

            result.IsSuccess.Should().BeTrue();
            var report = result.Value!;
            // Table rows: [0]=Total Headcount, [1]=Active, [2]=Inactive/Separated — cells keep natural types.
            report.Table.Rows[0].Should().BeEquivalentTo(new object?[] { "Total Headcount", 4 });
            report.Table.Rows[1].Should().BeEquivalentTo(new object?[] { "Active", 2 });          // Active + Probation
            report.Table.Rows[2].Should().BeEquivalentTo(new object?[] { "Inactive / Separated", 2 });
            // KPI summary (AC-2) surfaces total/active/inactive.
            report.Metadata.Summary.Single(s => s.Label == "Active").Value.Should().Be(2);
            report.Metadata.Summary.Single(s => s.Label == "Inactive / Separated").Value.Should().Be(2);
            report.Charts.Should().Contain(c => c.Title == "Headcount by Department");
        }
    }

    // ── AC-3/BR-3: turnover rate = 10% for 90 active + 10 terminated ─────────

    [Fact]
    public async Task Turnover_Rate_IsSeparationsOverAvgHeadcount_TimesHundred()
    {
        var dept = await SeedDepartment(_tenantA, "Engineering");
        var window = new HrReportQueryParams
        {
            DateFrom = new DateTime(2026, 1, 1),
            DateTo = new DateTime(2026, 12, 31),
        };

        // 90 still-active employees.
        for (int i = 0; i < 90; i++)
            await SeedEmployee(_tenantA, $"ACT{i}", dept, EmployeeStatus.Active);

        // 10 terminated employees, each with an in-window Status separation entry.
        for (int i = 0; i < 10; i++)
        {
            var id = await SeedEmployee(_tenantA, $"SEP{i}", dept, EmployeeStatus.Terminated);
            await SeedSeparation(_tenantA, id, new DateTime(2026, 6, 15));
        }

        var (db, svc) = Scope(_tenantA);
        using (db)
        {
            var result = await svc.GenerateReportAsync(HrReportType.EmployeeTurnover, window);

            result.IsSuccess.Should().BeTrue();
            var rows = result.Value!.Table.Rows;
            // avg headcount = 90 active + 10 separations = 100; rate = 10 / 100 * 100 = 10%.
            rows.Single(r => (string?)r[0] == "Total Separations")[1].Should().Be(10);
            rows.Single(r => (string?)r[0] == "Turnover Rate %")[1].Should().Be(10m);
            // KPI summary surfaces the same headline numbers (AC-3).
            result.Value.Metadata.Summary.Single(s => s.Label == "Total Separations").Value.Should().Be(10);
            result.Value.Metadata.Summary.Single(s => s.Label == "Turnover Rate %").Value.Should().Be(10m);
        }
    }

    // ── FR-2: department filter restricts the population ────────────────────

    [Fact]
    public async Task DepartmentFilter_RestrictsPopulation()
    {
        var eng = await SeedDepartment(_tenantA, "Engineering");
        var sales = await SeedDepartment(_tenantA, "Sales");
        await SeedEmployee(_tenantA, "E1", eng);
        await SeedEmployee(_tenantA, "E2", eng);
        await SeedEmployee(_tenantA, "S1", sales);

        var (db, svc) = Scope(_tenantA);
        using (db)
        {
            var result = await svc.GenerateReportAsync(
                HrReportType.DepartmentDistribution,
                new HrReportQueryParams { DepartmentIds = [eng] });

            result.IsSuccess.Should().BeTrue();
            // Only Engineering's 2 active employees are counted.
            result.Value!.Metadata.Summary.Single(s => s.Label == "Total Active Headcount").Value.Should().Be(2);
            result.Value.Table.Rows.Should().OnlyContain(r => (string?)r[0] == "Engineering");
            result.Value.Table.Rows.Single()[1].Should().Be(2);
        }
    }
}
