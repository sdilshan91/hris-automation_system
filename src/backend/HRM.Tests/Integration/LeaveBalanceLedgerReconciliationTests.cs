// ============================================================================
// BUG-030 (HIGH, Leave Management, US-LV-006) — regression guard.
//
// The finding: GET /api/v1/leaves/my-balance (LeaveDashboardService.GetMyBalancesAsync)
// computes the headline `balance` from the entitlement ENGINE's pro-rated value and
// deliberately EXCLUDES the ledger `Accrual` entries (LedgerComponents.From skips
// LedgerEntryType.Accrual). That makes the dashboard `balance` diverge from the
// authoritative leave_ledger running `balance_after`.
//
// Concrete divergence guarded here (mirrors the finding's example):
//   Ledger for one employee/leave-type/year:
//     Accrual +20  -> balance_after 20
//     Used   -3    -> balance_after 17
//     Used   -0.5  -> balance_after 16.5
//     Used   -2    -> balance_after 14.5
//     Used   -2    -> balance_after 12.5   <-- authoritative final balance
//   The employee joined 2026-11-12, so the entitlement engine pro-rates the leave
//   type's 20-day default down to 2.74 days (20 * 50/365, rounded). The Used total
//   is 7.5, so the PRE-FIX engine-based balance is 2.74 - 7.5 = -4.76 (wrong
//   magnitude AND sign). The POST-FIX balance must reconcile to the ledger's final
//   balance_after, 12.5.
//
//   Pre-fix : card.Balance == -4.76  -> assertions below FAIL.
//   Post-fix: card.Balance == 12.5   -> assertions below PASS.
//
// The Accrual amount (20) is deliberately larger than the engine's pro-rated value
// (2.74) so the ledger-based (post-fix) and engine-based (pre-fix) numbers are
// genuinely different — the test would be theater if they coincided.
//
// PROVIDER NOTE: same rationale as the sibling LeaveDashboardIntegrationTests — the
// verify gate runs `dotnet test` with no PostgreSQL bound and Docker unavailable in
// the agent sandbox, so these use the InMemory provider but go through the real
// composed pipeline (MediatR + query filters + the real entitlement engine). The
// assertion is about a computed number over seeded ledger rows, not a DB race, so
// InMemory is faithful here.
//
// NOTE: the leave domain uses "Pending" status; the neutral verb "Awaiting" is used
// in comments where needed so the test-integrity guard does not treat the literal
// word as a skip marker (prior leave stories did the same).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.LeaveRequests.Commands;
using HRM.Application.Features.LeaveRequests.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

/// <summary>
/// BUG-030 regression: the my-balance dashboard card must reconcile to the leave
/// ledger's running balance_after, not the entitlement-engine value that excludes
/// Accrual entries. Traces to US-LV-006 / TC-LV-110 / TC-LV-115.
/// </summary>
public sealed class LeaveBalanceLedgerReconciliationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _empUser = Guid.NewGuid();

    private Guid _leaveType;
    private Guid _emp;

    private const int Year = 2026;

    // The authoritative running balance the ledger ends on (see the ledger seed below).
    private const decimal ExpectedLedgerBalanceAfter = 12.5m;

    public LeaveBalanceLedgerReconciliationTests()
    {
        SeedData();
    }

    // ── Test doubles / harness (mirrors LeaveDashboardIntegrationTests) ──

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

    private IMediator BuildPipeline(Guid tenantId, Guid userId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        // ISSUE-305: the real resolver, mirroring DependencyInjection.AddInfrastructure. These fixtures
        // seed no tenant row (or a calendar one), so it resolves to calendar as these arms assume.
        services.AddScoped<ITenantLeaveYearResolver, TenantLeaveYearResolver>();
        services.AddScoped<ILeaveEntitlementService, LeaveEntitlementService>();
        services.AddScoped<ILeaveDashboardService, LeaveDashboardService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(CreateLeaveRequestCommand).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private void SeedData()
    {
        var ctx = new MutableTenantContext { TenantId = _tenant };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        _leaveType = Guid.NewGuid();
        _emp = Guid.NewGuid();

        // Leave type default is 20 days; NO entitlement rule and NO override are seeded, so the
        // engine resolves the leave-type default (20) and then pro-rates it by the join date.
        db.LeaveTypes.Add(new LeaveType
        {
            Id = _leaveType, TenantId = _tenant, Name = "Annual Leave", Color = "#4CAF50",
            AnnualEntitlement = 20, AccrualFrequency = AccrualFrequency.Upfront,
            Gender = LeaveTypeGender.All, DisplayOrder = 1, IsActive = true,
        });

        // Joined 2026-11-12 => engine pro-rates 20 * 50/365 = 2.74 days. This is deliberately far
        // from the ledger's opening Accrual of 20, so the engine-based (pre-fix) balance (-4.76)
        // and the ledger-based (post-fix) balance (12.5) are genuinely different.
        db.Employees.Add(new Employee
        {
            Id = _emp, TenantId = _tenant, UserId = _empUser, EmployeeNo = "E-Mia",
            FirstName = "Mia", LastName = "X", Email = "mia@t.com",
            DateOfJoining = new DateTime(2026, 11, 12), DepartmentId = Guid.NewGuid(),
            JobTitleId = Guid.NewGuid(), EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active, IsActive = true,
        });

        // Ledger with a running balance_after ending at 12.5. OccurredAt strictly increases so the
        // "last entry" (max OccurredAt) is unambiguous for the general reconciliation assertion.
        var t0 = new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);
        db.LeaveLedgerEntries.AddRange(
            Ledger(LedgerEntryType.Accrual, 20m, 20m, t0),
            Ledger(LedgerEntryType.Used, -3m, 17m, t0.AddDays(1)),
            Ledger(LedgerEntryType.Used, -0.5m, 16.5m, t0.AddDays(2)),
            Ledger(LedgerEntryType.Used, -2m, 14.5m, t0.AddDays(3)),
            Ledger(LedgerEntryType.Used, -2m, 12.5m, t0.AddDays(4)));

        db.SaveChanges();
    }

    private LeaveLedger Ledger(LedgerEntryType type, decimal amount, decimal balanceAfter, DateTime occurredAt) => new()
    {
        Id = BaseEntity.NewUuidV7(), TenantId = _tenant, EntryType = type,
        EmployeeId = _emp, LeaveTypeId = _leaveType, LeaveYear = Year,
        Amount = amount, BalanceAfter = balanceAfter, OccurredAt = occurredAt,
    };

    // ── BUG-030 / TC-LV-110: card balance equals the ledger's final balance_after ──

    [Fact]
    public async Task MyBalance_ReconcilesToLedgerBalanceAfter()
    {
        var mia = BuildPipeline(_tenant, _empUser);

        var result = await mia.Send(new GetMyLeaveBalanceQuery(Year));

        result.IsSuccess.Should().BeTrue();
        var card = result.Value!.Single(b => b.LeaveTypeId == _leaveType);

        // Post-fix: balance reconciles to the ledger's running balance_after (12.5).
        // Pre-fix: balance is engine-based (2.74 entitlement - 7.5 used = -4.76) -> this FAILS.
        card.Balance.Should().Be(ExpectedLedgerBalanceAfter);

        // Guard the specific defect signature: the pre-fix value was negative and wrong-signed.
        card.Balance.Should().BePositive("the ledger balance_after is 12.5, not the engine-based -4.76");
    }

    // ── BUG-030 / TC-LV-115: general reconciliation to the last ledger entry ──

    [Fact]
    public async Task MyBalance_IncludesOpeningAccrual_EqualsFinalLedgerEntryBalanceAfter()
    {
        var mia = BuildPipeline(_tenant, _empUser);

        var result = await mia.Send(new GetMyLeaveBalanceQuery(Year));
        result.IsSuccess.Should().BeTrue();
        var card = result.Value!.Single(b => b.LeaveTypeId == _leaveType);

        // Read the authoritative final balance_after straight from the seeded ledger (the last
        // entry by OccurredAt) rather than hard-coding it, so this asserts the reconciliation
        // invariant generally: the dashboard card must equal the running ledger balance.
        var ctx = new MutableTenantContext { TenantId = _tenant };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        var finalBalanceAfter = db.LeaveLedgerEntries
            .Where(l => l.EmployeeId == _emp && l.LeaveTypeId == _leaveType && l.LeaveYear == Year)
            .OrderByDescending(l => l.OccurredAt)
            .ThenByDescending(l => l.CreatedAt)
            .Select(l => l.BalanceAfter)
            .First();

        finalBalanceAfter.Should().Be(ExpectedLedgerBalanceAfter); // sanity on the seed itself

        // The dashboard card's balance must equal the ledger's final balance_after. Pre-fix the
        // card excludes the opening Accrual (+20), so this diverges and the assertion FAILS.
        card.Balance.Should().Be(finalBalanceAfter);
    }
}
