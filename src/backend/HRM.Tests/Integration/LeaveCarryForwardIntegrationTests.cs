// ============================================================================
// US-LV-008: Leave carry-forward / expiry integration tests.
//
// Exercises the full service -> DbContext path and the MediatR preview query through
// a real DI container with ITenantContext-driven global query filters:
//   - Year-end job creates correct CarryForward + Expired ledger entries + tracking rows.
//   - Idempotent re-run creates nothing further (NFR-3).
//   - Tenant isolation: Tenant A and Tenant B processed independently, no cross-contamination.
//   - Preview query returns projected amounts and matches the job (FR-5, AC-5).
//
// Provider note (same as the other leave integration tests): the verify gate runs
// `dotnet test` on EF Core InMemory (no Docker/Postgres in the agent sandbox);
// PostgreSQL-specific schema is validated by the separate `migrations` CI job applying
// the generated migration to real PG. A real LeaveEntitlementService resolves the leave
// type default entitlement here (no rules/overrides seeded -> the type default applies).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.LeaveCarryForward.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class LeaveCarryForwardIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private Guid _leaveTypeA;
    private Guid _employeeA;
    private Guid _leaveTypeB;
    private Guid _employeeB;

    private const int FromYear = 2026;
    private const int ToYear = 2027;

    public LeaveCarryForwardIntegrationTests()
    {
        SeedTwoTenants();
    }

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

    private ServiceProvider BuildProvider(Guid tenantId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.Email.Returns("hr@test.com");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);

        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));

        services.AddScoped<ILeaveEntitlementService, LeaveEntitlementService>();
        services.AddScoped<ILeaveCarryForwardService, LeaveCarryForwardService>();

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(GetCarryForwardPreviewQuery).Assembly));

        return services.BuildServiceProvider();
    }

    private void SeedTwoTenants()
    {
        _leaveTypeA = Guid.NewGuid();
        _employeeA = Guid.NewGuid();
        _leaveTypeB = Guid.NewGuid();
        _employeeB = Guid.NewGuid();

        SeedTenant(_tenantA, _leaveTypeA, _employeeA, "A-1", usedDays: 6m); // 14 - 6 = 8 unused
        SeedTenant(_tenantB, _leaveTypeB, _employeeB, "B-1", usedDays: 2m); // 14 - 2 = 12 unused
    }

    private void SeedTenant(Guid tenant, Guid leaveType, Guid employee, string empNo, decimal usedDays)
    {
        var ctx = new MutableTenantContext { TenantId = tenant };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        db.LeaveTypes.Add(new LeaveType
        {
            Id = leaveType, TenantId = tenant, Name = "Annual Leave",
            AnnualEntitlement = 14m, AccrualFrequency = AccrualFrequency.Upfront,
            CarryForwardLimit = 5m, CarryForwardExpiryMonths = 3,
            Gender = LeaveTypeGender.All, IsActive = true,
        });

        db.Employees.Add(new Employee
        {
            Id = employee, TenantId = tenant, UserId = Guid.NewGuid(),
            EmployeeNo = empNo, FirstName = "Emp", LastName = empNo, Email = $"{empNo}@x.com",
            DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });

        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenant,
            EntryType = LedgerEntryType.Used, EmployeeId = employee, LeaveTypeId = leaveType,
            LeaveYear = FromYear, Amount = -usedDays, BalanceAfter = 14m - usedDays,
            OccurredAt = new DateTime(FromYear, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        db.SaveChanges();
    }

    private async Task<List<LeaveLedger>> Ledger(Guid tenant, Guid employee, Guid leaveType, int year)
    {
        var ctx = new MutableTenantContext { TenantId = tenant };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);
        return await db.LeaveLedgerEntries
            .Where(l => l.EmployeeId == employee && l.LeaveTypeId == leaveType && l.LeaveYear == year)
            .ToListAsync();
    }

    private async Task<List<LeaveCarryForwardTracking>> Tracking(Guid tenant, Guid employee)
    {
        var ctx = new MutableTenantContext { TenantId = tenant };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);
        return await db.LeaveCarryForwardTrackings
            .Where(t => t.EmployeeId == employee).ToListAsync();
    }

    // ── Year-end job creates correct ledger + tracking rows ─────────

    [Fact]
    public async Task YearEnd_CreatesCarryForwardAndExpiredLedgerAndTracking()
    {
        using var provider = BuildProvider(_tenantA);
        var service = provider.GetRequiredService<ILeaveCarryForwardService>();

        var result = await service.ProcessYearEndAsync(FromYear);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);

        // 8 unused, limit 5 -> carry 5 (new year), expire 3 (closing year).
        var carry = (await Ledger(_tenantA, _employeeA, _leaveTypeA, ToYear))
            .Single(l => l.EntryType == LedgerEntryType.CarryForward);
        carry.Amount.Should().Be(5m);

        var expired = (await Ledger(_tenantA, _employeeA, _leaveTypeA, FromYear))
            .Single(l => l.EntryType == LedgerEntryType.Expired);
        expired.Amount.Should().Be(-3m);

        (await Tracking(_tenantA, _employeeA)).Single().CarriedDays.Should().Be(5m);
    }

    // ── Idempotent re-run (NFR-3) ───────────────────────────────────

    [Fact]
    public async Task YearEnd_RerunCreatesNothingFurther()
    {
        using var provider = BuildProvider(_tenantA);
        var service = provider.GetRequiredService<ILeaveCarryForwardService>();

        await service.ProcessYearEndAsync(FromYear);
        var second = await service.ProcessYearEndAsync(FromYear);

        second.Value.Should().Be(0);
        (await Tracking(_tenantA, _employeeA)).Should().ContainSingle();
        (await Ledger(_tenantA, _employeeA, _leaveTypeA, ToYear))
            .Count(l => l.EntryType == LedgerEntryType.CarryForward).Should().Be(1);
    }

    // ── Tenant isolation (Test Hint) ────────────────────────────────

    [Fact]
    public async Task YearEnd_TenantsProcessedIndependently_NoCrossContamination()
    {
        // Process only Tenant A.
        using (var providerA = BuildProvider(_tenantA))
        {
            await providerA.GetRequiredService<ILeaveCarryForwardService>().ProcessYearEndAsync(FromYear);
        }

        // Tenant A has its carry-forward; Tenant B is untouched.
        (await Tracking(_tenantA, _employeeA)).Should().ContainSingle();
        (await Tracking(_tenantB, _employeeB)).Should().BeEmpty();

        // Now process Tenant B independently; its own numbers (12 unused -> carry 5, forfeit 7).
        using (var providerB = BuildProvider(_tenantB))
        {
            await providerB.GetRequiredService<ILeaveCarryForwardService>().ProcessYearEndAsync(FromYear);
        }

        (await Ledger(_tenantB, _employeeB, _leaveTypeB, ToYear))
            .Single(l => l.EntryType == LedgerEntryType.CarryForward).Amount.Should().Be(5m);
        (await Ledger(_tenantB, _employeeB, _leaveTypeB, FromYear))
            .Single(l => l.EntryType == LedgerEntryType.Expired).Amount.Should().Be(-7m);

        // Tenant A's tracking is still a single row (B's run did not touch A).
        (await Tracking(_tenantA, _employeeA)).Should().ContainSingle();
    }

    // ── Preview query returns projected amounts (FR-5, AC-5) ────────

    [Fact]
    public async Task PreviewQuery_ReturnsProjectedAmounts()
    {
        using var provider = BuildProvider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new GetCarryForwardPreviewQuery(FromYear));

        result.IsSuccess.Should().BeTrue();
        var row = result.Value!.Single();
        row.EmployeeId.Should().Be(_employeeA);
        row.CarryForward.Should().Be(5m);
        row.Forfeited.Should().Be(3m);
        row.UnusedBalance.Should().Be(8m);

        // Preview committed nothing.
        (await Tracking(_tenantA, _employeeA)).Should().BeEmpty();
    }

    [Fact]
    public async Task PreviewQuery_IsTenantScoped()
    {
        using var provider = BuildProvider(_tenantB);
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new GetCarryForwardPreviewQuery(FromYear));

        // Tenant B sees only its own employee, not Tenant A's.
        result.Value!.Should().ContainSingle(r => r.EmployeeId == _employeeB);
        result.Value.Should().NotContain(r => r.EmployeeId == _employeeA);
    }
}
