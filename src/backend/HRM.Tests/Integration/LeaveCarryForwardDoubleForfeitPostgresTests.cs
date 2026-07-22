// ============================================================================
// DF-65-pg: real-Postgres arm for the DF-65 double-forfeiture (year-end sweep must terminalize the
// prior ToYear==fromYear carry bucket so ProcessExpiryAsync cannot re-forfeit its un-consumed carried
// days). The 3 committed DF-65 arms live in Unit/LeaveCarryForwardServiceTests.cs and run
// InMemory-through-real-EF; no dedicated Testcontainers arm mirrored the full prior-bucket -> sweep ->
// expiry scenario on real PG, and the PooledLeaveLedger.PoolRowTickOffset µs-truncation path
// (PooledLeaveLedger.cs:26-29 const + :114 occurredAt.AddTicks) is exercised by no DF-65 arm — it only
// manifests on real PostgreSQL timestamptz microsecond truncation (InMemory keeps full tick precision).
//
// This class adds both on postgres:17-alpine, mirroring FinalSettlementPostgresTests' IAsyncLifetime
// fixture idiom:
//   (1) YearEndSweep_SupersedesPriorBucket_ExpiryDoesNotDoubleForfeit_OnPostgres — the load-bearing
//       DF-65 (b) assertion: the sweep terminalizes the still-Active prior bucket, so a later expiry run
//       forfeits the carried days EXACTLY ONCE. Mutation-sensitive: reverting the (b) supersede leaves the
//       prior bucket Active, expiry re-forfeits its remaining 1 carried day, and both asserts red.
//   (2) PooledDeduction_TwoRowSplit_AccrualRowSortsLatest_OnPostgres — proves the +PoolRowTickOffset
//       (1µs) survives PG timestamptz truncation: after a two-row pooled split (carry row + accrual row),
//       the accrual row (occurredAt + 1µs) still sorts latest for the OccurredAt-DESC running-balance read,
//       so the balance picks the correct final BalanceAfter. Mutation-sensitive to PoolRowTickOffset = 0.
//
// The (b) mutation-sensitivity (prior bucket must RETAIN un-forfeited carried days after the sweep) and a
// two-row pooled split (forfeit must EXHAUST the bucket, spilling into the accrual pool) are mutually
// exclusive within a single forfeit draw, so the tick-offset path is proven in its own focused fact
// rather than being forced into the double-forfeit scenario.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
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

[Trait("TC", "TC-LV-151")] // DF-65 double-forfeiture surfaces through the monthly expiry job (US-LV-008).
public sealed class LeaveCarryForwardDoubleForfeitPostgresTests : IAsyncLifetime
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

    private AppDbContext CreateContext()
    {
        var tc = new MutableTenantContext { TenantId = _tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
            .Options, tc);
    }

    private LeaveCarryForwardService BuildYearEndService(AppDbContext db, Guid empId, Guid typeId, decimal entitlement)
    {
        var tc = new MutableTenantContext { TenantId = _tenantId };
        // Stub the US-LV-002 engine so ProratedEntitlementDays is fixed independently of the accrual ledger —
        // exactly as the committed DF-65 unit arms do. That decoupling is the DF-62-parity lever too.
        var engine = Substitute.For<ILeaveEntitlementService>();
        engine.ComputeEffectiveEntitlementAsync(empId, typeId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<EffectiveEntitlementDto>.Success(new EffectiveEntitlementDto
            {
                EmployeeId = empId, LeaveTypeId = typeId, LeaveYear = 2026,
                BaseEntitlementDays = entitlement, ProratedEntitlementDays = entitlement, Source = "leave_type_default",
            }));
        return new LeaveCarryForwardService(
            db, tc, engine, NullLogger<LeaveCarryForwardService>.Instance, new TenantLeaveYearResolver(db, tc));
    }

    private static Employee NewEmployee(Guid tenantId, Guid deptId, Guid jobId, string no) => new()
    {
        Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeNo = no, FirstName = "P", LastName = no,
        Email = $"{no}@t.com", DateOfJoining = new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        DepartmentId = deptId, JobTitleId = jobId, EmploymentType = EmploymentType.FullTime,
        Status = EmployeeStatus.Active, IsActive = true,
    };

    private async Task<(Guid deptId, Guid jobId)> SeedTenantScaffoldAsync(AppDbContext db)
    {
        var deptId = BaseEntity.NewUuidV7();
        var jobId = BaseEntity.NewUuidV7();
        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme", DefaultCountryCode = "LK" });
        db.Departments.Add(new Department { Id = deptId, TenantId = _tenantId, Name = "Ops", Code = "OPS", IsActive = true });
        db.JobTitles.Add(new JobTitle { Id = jobId, TenantId = _tenantId, TitleName = "Engineer", IsActive = true });
        await db.SaveChangesAsync();
        return (deptId, jobId);
    }

    // ── (1) Prior bucket -> sweep -> expiry: the carried days are forfeited exactly once ──────────────
    [Fact]
    public async Task YearEndSweep_SupersedesPriorBucket_ExpiryDoesNotDoubleForfeit_OnPostgres()
    {
        await using (var mig = CreateContext()) await mig.Database.MigrateAsync();

        Guid empId, typeId;
        Guid priorBucketId;
        await using (var seed = CreateContext())
        {
            var (deptId, jobId) = await SeedTenantScaffoldAsync(seed);
            var emp = NewEmployee(_tenantId, deptId, jobId, "PG-DF65");
            empId = emp.Id;
            seed.Employees.Add(emp);

            typeId = BaseEntity.NewUuidV7();
            seed.LeaveTypes.Add(new LeaveType
            {
                Id = typeId, TenantId = _tenantId, Name = "Annual",
                AnnualEntitlement = 3m, AccrualFrequency = AccrualFrequency.Upfront,
                CarryForwardLimit = 5m, CarryForwardExpiryMonths = 12, // >= 12 => bucket still Active at the 2026 close.
                Encashable = false, NegativeBalanceAllowed = false, Gender = LeaveTypeGender.All, IsActive = true,
            });

            // Prior bucket carried 5 days from 2025 INTO 2026 (ToYear == fromYear), 1 already consumed (partial),
            // expiring 2027-01-01 (AFTER the 2026 close, so no monthly expiry terminalized it first).
            priorBucketId = BaseEntity.NewUuidV7();
            seed.LeaveCarryForwardTrackings.Add(new LeaveCarryForwardTracking
            {
                Id = priorBucketId, TenantId = _tenantId, EmployeeId = empId, LeaveTypeId = typeId,
                FromYear = 2025, ToYear = 2026, CarriedDays = 5m, ConsumedDays = 1m, ExpiredDays = 0m,
                ExpiryDate = new DateOnly(2027, 1, 1), Status = CarryForwardTrackingStatus.Active,
            });
            // The matching carry-in credit so 2026's unused balance actually sees those carried days.
            seed.LeaveLedgerEntries.Add(new LeaveLedger
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EntryType = LedgerEntryType.CarryForward,
                EmployeeId = empId, LeaveTypeId = typeId, LeaveYear = 2026, Amount = 5m, BalanceAfter = 5m,
                OccurredAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            await seed.SaveChangesAsync();
        }

        // 2026 unused = entitlement 3 + carry-in 5 = 8; limit 5 -> carry 5 into 2027, forfeit 3.
        await using (var act = CreateContext())
        {
            var svc = BuildYearEndService(act, empId, typeId, entitlement: 3m);
            var run = await svc.ProcessYearEndAsync(2026);
            run.IsSuccess.Should().BeTrue(run.Error);
        }

        await using (var check = CreateContext())
        {
            var b1 = await check.LeaveCarryForwardTrackings.AsNoTracking().FirstAsync(t => t.Id == priorBucketId);
            b1.Status.Should().Be(CarryForwardTrackingStatus.Consumed,
                "DF-65 (b): the sweep terminalizes the superseded prior-year bucket so expiry skips it");
            // (a) belt-and-suspenders: the pool-routed forfeit drew its 3 carried days from B1's pool (1 + 3 = 4).
            b1.ConsumedDays.Should().Be(4m,
                "the forfeit routed through PooledLeaveLedger bumped ConsumedDays by the 3 forfeited carried days");
            // B1 retains 5 - 4 = 1 un-forfeited carried day — the day a reverted (b) would double-forfeit below.

            var forfeitedBySweep = await check.LeaveLedgerEntries.AsNoTracking()
                .Where(l => l.EmployeeId == empId && l.LeaveTypeId == typeId && l.LeaveYear == 2026
                            && l.EntryType == LedgerEntryType.Expired)
                .SumAsync(l => -l.Amount);
            forfeitedBySweep.Should().Be(3m, "the sweep forfeits exactly unused(8) - limit(5) = 3");
        }

        // Monthly expiry runs AFTER the 2026 close (2027-02-01 >= B1.ExpiryDate 2027-01-01). WITH the (b)
        // supersede B1 is terminal, so nothing is re-forfeited. WITHOUT it, B1 is Active + due and expiry
        // re-forfeits its remaining 1 carried day (the DF-65 double-forfeit, employee detriment).
        await using (var expire = CreateContext())
        {
            var svc = BuildYearEndService(expire, empId, typeId, entitlement: 3m);
            var expired = await svc.ProcessExpiryAsync(new DateOnly(2027, 2, 1));
            expired.IsSuccess.Should().BeTrue(expired.Error);
            expired.Value.Should().Be(0, "the superseded bucket is terminal, so expiry re-forfeits nothing");
        }

        await using (var final = CreateContext())
        {
            var totalForfeited = await final.LeaveLedgerEntries.AsNoTracking()
                .Where(l => l.EmployeeId == empId && l.LeaveTypeId == typeId && l.LeaveYear == 2026
                            && l.EntryType == LedgerEntryType.Expired)
                .SumAsync(l => -l.Amount);
            totalForfeited.Should().Be(3m,
                "the carried days are forfeited ONCE by the sweep, never a second time by expiry");
        }
    }

    // ── (2) Two-row pooled split: the accrual row's +1µs offset survives PG timestamptz truncation ────
    [Fact]
    public async Task PooledDeduction_TwoRowSplit_AccrualRowSortsLatest_OnPostgres()
    {
        await using (var mig = CreateContext()) await mig.Database.MigrateAsync();

        Guid empId, typeId;
        var occurredAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await using (var seed = CreateContext())
        {
            var (deptId, jobId) = await SeedTenantScaffoldAsync(seed);
            var emp = NewEmployee(_tenantId, deptId, jobId, "PG-TICK");
            empId = emp.Id;
            seed.Employees.Add(emp);

            typeId = BaseEntity.NewUuidV7();
            seed.LeaveTypes.Add(new LeaveType
            {
                Id = typeId, TenantId = _tenantId, Name = "Annual", AnnualEntitlement = 10m,
                AccrualFrequency = AccrualFrequency.Upfront, CarryForwardLimit = 5m,
                Gender = LeaveTypeGender.All, IsActive = true,
            });
            // An Active carry bucket with only 2 carried days remaining, so a 3-day draw spills 1 into accrual.
            seed.LeaveCarryForwardTrackings.Add(new LeaveCarryForwardTracking
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeId = empId, LeaveTypeId = typeId,
                FromYear = 2025, ToYear = 2026, CarriedDays = 2m, ConsumedDays = 0m, ExpiredDays = 0m,
                ExpiryDate = new DateOnly(2027, 1, 1), Status = CarryForwardTrackingStatus.Active,
            });
            await seed.SaveChangesAsync();
        }

        // Draw 3 days from a running balance of 10: carry pool covers 2 (BalanceAfter 8 @ occurredAt),
        // accrual pool covers the remaining 1 (BalanceAfter 7 @ occurredAt + PoolRowTickOffset).
        await using (var act = CreateContext())
        {
            await PooledLeaveLedger.AppendDeductionAsync(
                act, _tenantId, empId, typeId, leaveYear: 2026, totalDays: 3m, leaveRequestId: null,
                currentBalance: 10m, description: "tick-offset probe", occurredAt,
                LedgerEntryType.Used, CancellationToken.None);
            await act.SaveChangesAsync();
        }

        await using (var read = CreateContext())
        {
            var rows = await read.LeaveLedgerEntries.AsNoTracking()
                .Where(l => l.EmployeeId == empId && l.LeaveTypeId == typeId && l.LeaveYear == 2026)
                .ToListAsync();
            rows.Should().HaveCount(2);
            rows.Should().Contain(l => l.Pool == LeavePool.CarryForward && l.Amount == -2m && l.BalanceAfter == 8m);
            rows.Should().Contain(l => l.Pool == LeavePool.Accrual && l.Amount == -1m && l.BalanceAfter == 7m);

            // Deterministic tick-offset proof (independent of PG's ORDER BY tie-break): the persisted OccurredAt
            // values themselves must diverge after the timestamptz round-trip. With PoolRowTickOffset=0 the two
            // rows would be byte-equal on OccurredAt (AND on CreatedAt — AuditInterceptor stamps one batch timestamp),
            // so BeAfter reds deterministically. PG truncates < 10 ticks (1µs) to 0µs, so the >= 10-tick delta is
            // the exact survivable increment — this kills the offset=0 mutant without relying on row order.
            var carryRow = rows.Single(r => r.Pool == LeavePool.CarryForward);
            var accrualRow = rows.Single(r => r.Pool == LeavePool.Accrual);
            accrualRow.OccurredAt.Should().BeAfter(carryRow.OccurredAt,
                "the +PoolRowTickOffset (1µs) must survive PostgreSQL timestamptz microsecond truncation");
            (accrualRow.OccurredAt - carryRow.OccurredAt).Should().BeGreaterThanOrEqualTo(TimeSpan.FromTicks(10));

            // The running-balance read (OccurredAt DESC, CreatedAt DESC — as GetLedgerBalanceAsync does). On PG the
            // two OccurredAt values differ by 1µs (PoolRowTickOffset survives timestamptz truncation), so the
            // accrual row wins and the balance is the correct final 7 — not the carry row's intermediate 8.
            var latest = await read.LeaveLedgerEntries.AsNoTracking()
                .Where(l => l.EmployeeId == empId && l.LeaveTypeId == typeId && l.LeaveYear == 2026)
                .OrderByDescending(l => l.OccurredAt)
                .ThenByDescending(l => l.CreatedAt)
                .FirstAsync();
            latest.Pool.Should().Be(LeavePool.Accrual,
                "the +PoolRowTickOffset (1µs) offset keeps the accrual row latest after PG timestamptz truncation");
            latest.BalanceAfter.Should().Be(7m, "the running-balance read must pick the final chained balance");
        }
    }
}
