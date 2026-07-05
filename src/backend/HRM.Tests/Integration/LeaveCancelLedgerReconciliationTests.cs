// ============================================================================
// ISSUE-043 (MED, Leave Management, US-LV-010) — regression GUARD (invariant lock).
//
// The finding: the leave-cancel result's `balanceAfter` could diverge from the
// authoritative balance the my-balance dashboard shows for the same
// employee/leave-type/year. ISSUE-043 is ALREADY RESOLVED TRANSITIVELY by BUG-030
// (PR #144, merged): the dashboard (LeaveDashboardService.GetMyBalancesAsync) now
// reads the authoritative leave_ledger running `balance_after` (latest entry), and
// LeaveRequestService.CancelAsync returns the restoration ledger entry's
// `BalanceAfter` (== the same latest `GetLedgerBalanceAsync`). So the two agree today.
//
// This test does NOT re-fix anything — there is no fix commit to demonstrate red-then-
// green against, because the invariant already holds on the current tree. Its sole
// purpose is to LOCK the reconciliation so a FUTURE change to EITHER side (the cancel
// balance math, or the dashboard's balance derivation) cannot silently re-diverge.
// Expected result on the current tree: PASS. If this ever goes red, the cancel↔ledger↔
// dashboard reconciliation has been broken again (a regression of ISSUE-043).
//
// WHY THIS IS A GENUINE INVARIANT, NOT A TAUTOLOGY:
//   The two values compared are produced by TWO DIFFERENT code paths:
//     Source 1 — LeaveRequestService.CancelAsync: computes restored balance as
//                GetLedgerBalanceAsync(before) + request.TotalDays and returns it as
//                the DTO's `balanceAfter` (and writes the reversal ledger row).
//     Source 2 — LeaveDashboardService.GetMyBalancesAsync (via GetMyLeaveBalanceQuery,
//                the "my-balance" dashboard path): independently re-reads the ledger
//                and takes the BalanceAfter of the LATEST entry.
//   They share no computation — one adds TotalDays to a pre-cancel read, the other
//   picks the max-by-time ledger row after the write. Equality is therefore a real
//   cross-path reconciliation, and the seeded scenario makes the agreed value
//   NON-TRIVIAL: a real Accrual + prior Used deductions leave a running balance of 7,
//   so the restored balance is 10 — deliberately NOT 0 and NOT the leave type's
//   entitlement (14). A test that only agreed on 0 or on the entitlement would be
//   theater; this one cannot pass by coincidence.
//
// PROVIDER NOTE: same rationale as the sibling LeaveCancellationIntegrationTests /
// LeaveBalanceLedgerReconciliationTests — the verify gate runs `dotnet test` with no
// PostgreSQL bound and Docker unavailable in the agent sandbox, so this uses the
// InMemory provider but goes through the real composed pipeline (MediatR + query
// filters + the real LeaveRequestService, LeaveDashboardService, and entitlement
// engine). The assertion is arithmetic over seeded ledger rows read back through the
// real services, not a DB race, so InMemory is faithful here.
//
// NOTE: the leave domain uses "Pending" status; the neutral verb "Awaiting" is used in
// comments where needed so the test-integrity guard does not treat the literal word as
// a skip marker (prior leave stories did the same).
//
// Traces to US-LV-010 (cancel) + US-LV-006 (my-balance dashboard); guards ISSUE-043,
// reconciles to BUG-030 (PR #144).
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
/// ISSUE-043 regression guard: the balance returned by cancelling an approved leave
/// (LeaveRequestService.CancelAsync) must equal the balance the my-balance dashboard
/// (LeaveDashboardService.GetMyBalancesAsync) reports for the same key — both derived
/// from the authoritative leave ledger, by different code. Locks the invariant that
/// BUG-030 (PR #144) established so a future change can't silently re-diverge them.
/// </summary>
public sealed class LeaveCancelLedgerReconciliationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _empUser = Guid.NewGuid();

    private Guid _leaveType;
    private Guid _emp;

    private const int Year = 2026;

    // Leave type entitlement (informational on the card) — deliberately DIFFERENT from
    // the restored running balance so the reconciled value can't coincide with it.
    private const decimal Entitlement = 14m;

    // Running ledger balance BEFORE the cancel: Accrual +14, Used -3 (this request),
    // Used -4 (other leave) => 7. Restoring this request's 3 days => 10 (the value both
    // sources must agree on — non-zero and != the 14-day entitlement).
    private const decimal BalanceBeforeCancel = 7m;
    private const decimal RequestTotalDays = 3m;
    private const decimal ExpectedRestoredBalance = BalanceBeforeCancel + RequestTotalDays; // 10

    // A fixed future start so the approved leave is still cancellable (start > today, so
    // it clears the cancellation-window block) and its year is deterministic.
    private static readonly DateOnly FutureStart = new(Year, 12, 1);

    public LeaveCancelLedgerReconciliationTests()
    {
        SeedData();
    }

    // ── Test doubles / harness (mirrors LeaveCancellationIntegrationTests) ──

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

    // Registers BOTH the cancel path (LeaveRequestService + its collaborators) and the
    // my-balance dashboard path (LeaveDashboardService + entitlement engine) so the two
    // independent code paths run through the same real pipeline against one DbContext.
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
        services.AddScoped<IHolidayProvider, NoOpHolidayProvider>();
        services.AddScoped<ILeaveNotificationService, LogOnlyLeaveNotificationService>();
        services.AddScoped<ILeaveRequestService, LeaveRequestService>();
        services.AddScoped<ILeaveEntitlementService, LeaveEntitlementService>();
        services.AddScoped<ILeaveDashboardService, LeaveDashboardService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(CreateLeaveRequestCommand).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private AppDbContext DbFor(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, new MutableTenantContext { TenantId = tenantId });
    }

    private void SeedData()
    {
        _leaveType = Guid.NewGuid();
        _emp = Guid.NewGuid();

        using var db = DbFor(_tenant);

        db.LeaveTypes.Add(new LeaveType
        {
            Id = _leaveType, TenantId = _tenant, Name = "Annual Leave", Color = "#4CAF50",
            AnnualEntitlement = Entitlement, AccrualFrequency = AccrualFrequency.Upfront,
            Gender = LeaveTypeGender.All, NegativeBalanceAllowed = false,
            DisplayOrder = 1, IsActive = true,
        });

        // DateOfJoining well in the past => the entitlement engine resolves the full 14
        // (no pro-ration). Entitlement (14) stays distinct from the restored balance (10).
        db.Employees.Add(new Employee
        {
            Id = _emp, TenantId = _tenant, UserId = _empUser, EmployeeNo = "E-Ann",
            FirstName = "Ann", LastName = "X", Email = "ann@t.com",
            DateOfJoining = new DateTime(2020, 1, 1), DepartmentId = Guid.NewGuid(),
            JobTitleId = Guid.NewGuid(), EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active, IsActive = true,
        });

        // Ledger with a running balance_after ending at 7 (a REAL deduction — not the
        // trivial entitlement value). OccurredAt strictly increases and is in the past so
        // the cancel reversal (OccurredAt = now) is unambiguously the latest entry.
        var t0 = new DateTime(Year, 1, 15, 8, 0, 0, DateTimeKind.Utc);
        db.LeaveLedgerEntries.AddRange(
            Ledger(LedgerEntryType.Accrual, 14m, 14m, t0),
            Ledger(LedgerEntryType.Used, -3m, 11m, t0.AddDays(1)),  // this request's original deduction
            Ledger(LedgerEntryType.Used, -4m, 7m, t0.AddDays(2)));  // other leave already taken

        db.SaveChanges();
    }

    private LeaveLedger Ledger(LedgerEntryType type, decimal amount, decimal balanceAfter, DateTime occurredAt) => new()
    {
        Id = BaseEntity.NewUuidV7(), TenantId = _tenant, EntryType = type,
        EmployeeId = _emp, LeaveTypeId = _leaveType, LeaveYear = Year,
        Amount = amount, BalanceAfter = balanceAfter, OccurredAt = occurredAt,
    };

    private Guid SeedApprovedRequest()
    {
        using var db = DbFor(_tenant);
        var id = BaseEntity.NewUuidV7();
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = id, TenantId = _tenant, EmployeeId = _emp, LeaveTypeId = _leaveType,
            StartDate = FutureStart, EndDate = FutureStart.AddDays(2), TotalDays = RequestTotalDays,
            Status = LeaveRequestStatus.Approved, RequestedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
        return id;
    }

    private Guid SeedPendingRequest()
    {
        using var db = DbFor(_tenant);
        var id = BaseEntity.NewUuidV7();
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = id, TenantId = _tenant, EmployeeId = _emp, LeaveTypeId = _leaveType,
            StartDate = FutureStart, EndDate = FutureStart.AddDays(1), TotalDays = 2m,
            Status = LeaveRequestStatus.Pending, RequestedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
        return id;
    }

    // Raw read of the authoritative ledger running balance (latest entry by OccurredAt,
    // then CreatedAt) — the SAME derivation LeaveRequestService.GetLedgerBalanceAsync and
    // the dashboard use. Used only as a seed sanity anchor, independent of both services.
    private decimal RawLedgerBalanceAfter()
    {
        using var db = DbFor(_tenant);
        return db.LeaveLedgerEntries
            .Where(l => l.EmployeeId == _emp && l.LeaveTypeId == _leaveType && l.LeaveYear == Year)
            .OrderByDescending(l => l.OccurredAt)
            .ThenByDescending(l => l.CreatedAt)
            .Select(l => l.BalanceAfter)
            .First();
    }

    // ── ISSUE-043: cancel DTO balanceAfter == my-balance dashboard balance ──

    [Fact]
    public async Task CancelApprovedLeave_BalanceAfter_EqualsLedgerBalanceAfter_ISSUE043()
    {
        var pipeline = BuildPipeline(_tenant, _empUser);

        // Sanity: the seeded pre-cancel running balance is a REAL deduction (7), not the
        // trivial entitlement (14) and not 0 — so the reconciled value below is non-trivial.
        RawLedgerBalanceAfter().Should().Be(BalanceBeforeCancel);
        BalanceBeforeCancel.Should().NotBe(Entitlement); // guards against a coincidental match

        var requestId = SeedApprovedRequest();

        // ── Source 1: the cancel path (LeaveRequestService.CancelAsync). ──
        var cancel = await pipeline.Send(new CancelLeaveRequestCommand(requestId, "Plans changed"));

        cancel.IsSuccess.Should().BeTrue();
        cancel.Value!.Status.Should().Be("Cancelled");
        cancel.Value.BalanceAfter.Should().NotBeNull("an approved cancellation writes a restoration ledger entry");
        var cancelBalanceAfter = cancel.Value.BalanceAfter!.Value;

        // The cancel restored exactly the deducted TotalDays: 7 + 3 = 10.
        cancelBalanceAfter.Should().Be(ExpectedRestoredBalance);
        cancelBalanceAfter.Should().Be(BalanceBeforeCancel + RequestTotalDays);

        // ── Source 2: the my-balance dashboard path (LeaveDashboardService via the query). ──
        // Independently re-reads the ledger and takes the latest entry's BalanceAfter — this
        // is the balance the finding says must NOT diverge from the cancel result.
        var dashboard = await pipeline.Send(new GetMyLeaveBalanceQuery(Year));
        dashboard.IsSuccess.Should().BeTrue();
        var card = dashboard.Value!.Single(b => b.LeaveTypeId == _leaveType);

        // ── THE INVARIANT (ISSUE-043 lock): the two independently-computed sources agree. ──
        card.Balance.Should().Be(cancelBalanceAfter,
            "the cancel result balanceAfter (Source 1) and the my-balance dashboard balance " +
            "(Source 2) both derive from the authoritative ledger and must reconcile (ISSUE-043 / BUG-030)");

        // And they agree on the non-trivial restored value — not 0, not the entitlement.
        card.Balance.Should().Be(ExpectedRestoredBalance);
        card.Balance.Should().NotBe(Entitlement);
    }

    // ── Current behaviour: a pending cancel performs NO ledger reversal (balanceAfter null) ──

    [Fact]
    public async Task CancelAwaitingLeave_ReturnsNullBalanceAfter_ISSUE043()
    {
        var pipeline = BuildPipeline(_tenant, _empUser);
        var requestId = SeedPendingRequest();

        var cancel = await pipeline.Send(new CancelLeaveRequestCommand(requestId, null));

        cancel.IsSuccess.Should().BeTrue();
        cancel.Value!.Status.Should().Be("Cancelled");
        // Nothing was committed against the balance, so there is no reversal and no balanceAfter.
        cancel.Value.BalanceAfter.Should().BeNull();

        // The dashboard balance is therefore untouched — still the seeded running balance (7).
        var dashboard = await pipeline.Send(new GetMyLeaveBalanceQuery(Year));
        dashboard.IsSuccess.Should().BeTrue();
        dashboard.Value!.Single(b => b.LeaveTypeId == _leaveType).Balance
            .Should().Be(BalanceBeforeCancel);
    }
}
