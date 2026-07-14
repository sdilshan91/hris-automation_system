// ============================================================================
// ISSUE-294 (F&F Phase 1): the final_settlement idempotency guarantee + its dormant RLS policies are DB-only
// concerns the InMemory fast gate cannot prove. On real postgres:17-alpine this asserts:
//   (a) the UNIQUE index on offboarding_instance_id rejects a duplicate settlement with SQLSTATE 23505;
//   (b) the dormant tenant_isolation RLS policy exists for all three new tenant_id tables.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-PAY-FNF-002")]
public sealed class FinalSettlementPostgresTests : IAsyncLifetime
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

    private (ITenantContext tc, ICurrentUser cu) Actors()
    {
        var tc = new MutableTenantContext { TenantId = _tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        return (tc, cu);
    }

    private static FinalSettlement NewSettlement(Guid offboardingId) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        EmployeeId = Guid.NewGuid(),
        OffboardingInstanceId = offboardingId,
        LastWorkingDay = new DateOnly(2026, 6, 15),
        FiscalYear = string.Empty,
        ProRatedGross = 15_000m,
        NetPayable = 15_000m,
        PolicyEffectiveFrom = new DateOnly(2026, 1, 1),
        FinalPeriodOwnedBySettlement = true,
        ComputedAtUtc = DateTime.UtcNow,
        Status = FinalSettlementStatus.Computed,
    };

    [Fact]
    public async Task DuplicateOffboardingInstance_IsRejected_By23505_OnPostgres()
    {
        var (tc, cu) = Actors();
        await using var db = CreateContext(tc, cu);
        await db.Database.MigrateAsync();

        var offboardingId = Guid.NewGuid();

        await using (var db1 = CreateContext(tc, cu))
        {
            db1.FinalSettlements.Add(NewSettlement(offboardingId));
            await db1.SaveChangesAsync();
        }

        await using var db2 = CreateContext(tc, cu);
        db2.FinalSettlements.Add(NewSettlement(offboardingId)); // same offboarding instance → must be rejected.

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(async () => await db2.SaveChangesAsync());
        thrown.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be("23505", "the unique index on offboarding_instance_id enforces idempotency");
    }

    // A money figure computed end-to-end on REAL Postgres (the InMemory arms can mask an Npgsql LINQ-translation
    // or ordering divergence on the ledger-balance / component-effective queries that feed the encashment math).
    [Fact]
    public async Task Encashment_MoneyFigure_ComputedByTheService_OnPostgres()
    {
        var (tc, cu) = Actors();
        await using (var mig = CreateContext(tc, cu)) await mig.Database.MigrateAsync();

        var empId = BaseEntity.NewUuidV7();
        var offboardingId = Guid.NewGuid();
        await using (var seed = CreateContext(tc, cu))
        {
            seed.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme", DefaultCountryCode = "LK" });
            // Real Department + JobTitle rows — the employees FKs are enforced on Postgres (unlike InMemory).
            var deptId = BaseEntity.NewUuidV7();
            var jobId = BaseEntity.NewUuidV7();
            seed.Departments.Add(new Department { Id = deptId, TenantId = _tenantId, Name = "Ops", Code = "OPS", IsActive = true });
            seed.JobTitles.Add(new JobTitle { Id = jobId, TenantId = _tenantId, TitleName = "Engineer", IsActive = true });
            seed.Employees.Add(new Employee
            {
                Id = empId, TenantId = _tenantId, EmployeeNo = "PG-1", FirstName = "P", LastName = "G",
                Email = "pg@t.com", DateOfJoining = new DateTime(2020, 1, 1),
                DepartmentId = deptId, JobTitleId = jobId, EmploymentType = EmploymentType.FullTime,
                Status = EmployeeStatus.Active, IsActive = true,
            });
            var compId = BaseEntity.NewUuidV7();
            seed.SalaryComponents.Add(new SalaryComponent
            {
                Id = compId, TenantId = _tenantId, Name = "Basic Salary", Code = "BASIC",
                Type = SalaryComponentType.Earning, CalculationMethod = CalculationMethod.Fixed, IsActive = true, ProcessingOrder = 1,
            });
            seed.EmployeeSalaryComponents.Add(new EmployeeSalaryComponent
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeId = empId,
                SalaryStructureId = BaseEntity.NewUuidV7(), SalaryComponentId = compId,
                AnnualAmount = 360_000m, MonthlyAmount = 30_000m, EffectiveFrom = new DateOnly(2020, 1, 1), EffectiveTo = null,
            });
            var ltId = BaseEntity.NewUuidV7();
            seed.LeaveTypes.Add(new LeaveType
            {
                Id = ltId, TenantId = _tenantId, Name = "Annual", Encashable = true, IsActive = true,
                CarryForwardLimit = 5m, MaxEncashDays = 10m, AnnualEntitlement = 20m,
            });
            seed.LeaveLedgerEntries.Add(new LeaveLedger
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EntryType = LedgerEntryType.Accrual,
                EmployeeId = empId, LeaveTypeId = ltId, LeaveYear = 2026, Amount = 20m, BalanceAfter = 20m, OccurredAt = DateTime.UtcNow,
            });
            seed.TenantFnFPolicies.Add(new TenantFnFPolicy
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EffectiveFrom = new DateOnly(2026, 1, 1),
                IncludeProRatedFinalPay = true, IncludeStatutory = false, IncludeLeaveEncashment = true,
                FinalPeriodOwnedBySettlement = true, IsActive = true,
            });
            await seed.SaveChangesAsync();
        }

        Guid settlementId;
        await using (var act = CreateContext(tc, cu))
        {
            var svc = new HRM.Infrastructure.Services.RealPayrollFnFIntegration(
                act, tc, new HRM.Infrastructure.Services.StatutoryDeductionResolver(
                    act, tc, Microsoft.Extensions.Logging.Abstractions.NullLogger<HRM.Infrastructure.Services.StatutoryDeductionResolver>.Instance),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<HRM.Infrastructure.Services.RealPayrollFnFIntegration>.Instance);
            settlementId = await svc.TriggerFinalSettlementAsync(_tenantId, empId, offboardingId, new DateTime(2026, 6, 15));
        }

        await using var read = CreateContext(tc, cu);
        var s = await read.FinalSettlements.AsNoTracking().Include(x => x.Lines).FirstAsync(x => x.Id == settlementId);
        s.ProRatedGross.Should().Be(15_000m);                 // 30000 × 15/30.
        s.LeaveEncashmentTotal.Should().Be(10_000m);          // min(20−5, 10) = 10 days × (30000/30) = 10000.
        s.NetPayable.Should().Be(25_000m);
        s.Lines.Should().Contain(l => l.Type == FinalSettlementLineType.Encashment && l.Amount == 10_000m);
    }

    [Fact]
    public async Task DormantTenantIsolationPolicies_ExistForAllThreeTables_OnPostgres()
    {
        var (tc, cu) = Actors();
        await using var db = CreateContext(tc, cu);
        await db.Database.MigrateAsync();

        var policied = new HashSet<string>();
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT tablename FROM pg_policies WHERE schemaname = 'public' AND policyname = 'tenant_isolation' " +
            "AND tablename IN ('tenant_fnf_policy', 'final_settlement', 'final_settlement_line')", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) policied.Add(reader.GetString(0));

        policied.Should().BeEquivalentTo(new[] { "tenant_fnf_policy", "final_settlement", "final_settlement_line" },
            "every new tenant_id table ships its dormant tenant_isolation RLS policy (NEW-TENANT-TABLE rule)");
    }
}
