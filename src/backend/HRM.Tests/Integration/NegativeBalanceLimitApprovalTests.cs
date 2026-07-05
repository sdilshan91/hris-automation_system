// ============================================================================
// US-LV-006 / BUG-029: negative-balance FLOOR is enforced at approval.
//
// A leave type may permit a negative balance (NegativeBalanceAllowed = true) but
// still cap how far negative it may go via NegativeBalanceLimit. BUG-029: before the
// fix, LeaveRequestService.ApproveAsync only blocked a negative projection when the
// type disallowed negatives entirely — it NEVER checked the configured floor, so an
// approval could drive the balance arbitrarily negative. The fix rejects (400,
// "negative_balance_limit_exceeded") any approval whose projected balance would fall
// below -NegativeBalanceLimit, while still allowing it DOWN TO the floor (== -limit).
//
// These tests exercise the real handler -> service -> DbContext path through the composed
// MediatR pipeline (same harness rationale as LeaveApprovalIntegrationTests: InMemory
// provider, real ITenantContext global query filters).
//
//   - Approve_ExceedsNegativeBalanceLimit_IsRejected_BUG029 : projected < -limit -> 400,
//     rejected, request stays Pending, NO "Used" ledger row (balance unchanged).
//   - Approve_AtNegativeBalanceFloor_Succeeds_BUG029        : projected == -limit -> approved
//     (the floor is inclusive), a "Used" ledger row and the negative balance persisted.
//
// PRE-FIX: the floor check does not exist (verified via `git show HEAD:` — 0 occurrences of
// "negative_balance_limit_exceeded"), so the exceeds-limit approval SUCCEEDS instead of being
// rejected and the first test FAILS. POST-FIX it is rejected and the test passes. The floor
// (control) test passes both before and after — it guards against the fix over-rejecting.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.LeaveRequests.Commands;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class NegativeBalanceLimitApprovalTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _managerUser = Guid.NewGuid();

    private Guid _leaveType;   // NegativeBalanceAllowed = true, NegativeBalanceLimit = 2
    private Guid _manager;
    private Guid _report;

    private const decimal NegativeLimit = 2m; // the type's negative floor: balance may reach -2, no further
    private static readonly DateOnly Base = new(2026, 6, 1);

    public NegativeBalanceLimitApprovalTests() => SeedData();

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
        services.AddScoped<IHolidayProvider, NoOpHolidayProvider>();
        services.AddScoped<ILeaveNotificationService, LogOnlyLeaveNotificationService>();
        services.AddScoped<ILeaveRequestService, LeaveRequestService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(CreateLeaveRequestCommand).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private AppDbContext DbFor(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, new MutableTenantContext { TenantId = tenantId });
    }

    private void SeedData()
    {
        _leaveType = Guid.NewGuid();
        _manager = Guid.NewGuid();
        _report = Guid.NewGuid();

        using var db = DbFor(_tenantA);

        // A leave type that ALLOWS a negative balance but only DOWN TO -2 days.
        db.LeaveTypes.Add(new LeaveType
        {
            Id = _leaveType, TenantId = _tenantA, Name = "Annual Leave", Color = "#4CAF50",
            AnnualEntitlement = 14, AccrualFrequency = AccrualFrequency.Upfront, Gender = LeaveTypeGender.All,
            NegativeBalanceAllowed = true, NegativeBalanceLimit = NegativeLimit, IsActive = true,
        });

        db.Employees.AddRange(
            new Employee
            {
                Id = _manager, TenantId = _tenantA, UserId = _managerUser, EmployeeNo = "E-MGR",
                FirstName = "Mary", LastName = "X", Email = "mary@t.com", Status = EmployeeStatus.Active,
                DateOfJoining = new DateTime(2020, 1, 1), DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
                EmploymentType = EmploymentType.FullTime, IsActive = true,
            },
            new Employee
            {
                Id = _report, TenantId = _tenantA, EmployeeNo = "E-RPT", FirstName = "Ann", LastName = "X",
                Email = "ann@t.com", Status = EmployeeStatus.Active, ReportsToEmployeeId = _manager,
                DateOfJoining = new DateTime(2020, 1, 1), DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
                EmploymentType = EmploymentType.FullTime, IsActive = true,
            });

        // Balance near zero: exactly 1 day accrued for the report.
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, EntryType = LedgerEntryType.Accrual,
            EmployeeId = _report, LeaveTypeId = _leaveType, LeaveYear = Base.Year,
            Amount = 1m, BalanceAfter = 1m, OccurredAt = DateTime.UtcNow,
        });

        db.SaveChanges();
    }

    private Guid SeedPendingRequest(decimal totalDays)
    {
        using var db = DbFor(_tenantA);
        var id = BaseEntity.NewUuidV7();
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = id, TenantId = _tenantA, EmployeeId = _report, LeaveTypeId = _leaveType,
            StartDate = Base, EndDate = Base.AddDays((int)totalDays - 1), TotalDays = totalDays,
            Status = LeaveRequestStatus.Pending, RequestedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
        return id;
    }

    // ── BUG-029: approval that breaches the negative floor is rejected ──────

    [Fact]
    public async Task Approve_ExceedsNegativeBalanceLimit_IsRejected_BUG029()
    {
        // Balance = 1, request = 4 => projected = -3, which is beyond the -2 floor.
        var requestId = SeedPendingRequest(totalDays: 4m);
        var mediator = BuildPipeline(_tenantA, _managerUser);

        var result = await mediator.Send(new ApproveLeaveRequestCommand(requestId, "Approve please"));

        result.IsFailure.Should().BeTrue("projected -3 falls below the -2 negative-balance floor");
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("negative_balance_limit_exceeded");

        // Nothing was committed: the request stays Pending and no "Used" deduction was written,
        // so the ledger balance is unchanged.
        using var db = DbFor(_tenantA);
        db.LeaveRequests.Single(lr => lr.Id == requestId).Status.Should().Be(LeaveRequestStatus.Pending);
        db.LeaveLedgerEntries.Any(l => l.LeaveRequestId == requestId).Should().BeFalse();
        db.LeaveLedgerEntries
            .Where(l => l.EmployeeId == _report && l.LeaveTypeId == _leaveType)
            .Sum(l => l.Amount).Should().Be(1m, "the accrued balance must be untouched after a rejected approval");
    }

    // ── Control: approval DOWN TO the floor (projected == -limit) still succeeds ──

    [Fact]
    public async Task Approve_AtNegativeBalanceFloor_Succeeds_BUG029()
    {
        // Balance = 1, request = 3 => projected = -2, exactly the -2 floor (inclusive) => allowed.
        var requestId = SeedPendingRequest(totalDays: 3m);
        var mediator = BuildPipeline(_tenantA, _managerUser);

        var result = await mediator.Send(new ApproveLeaveRequestCommand(requestId, "Approved"));

        result.IsSuccess.Should().BeTrue(result.ErrorCode + ": " + result.Error);
        result.Value!.Status.Should().Be("Approved");
        result.Value.BalanceAfter.Should().Be(-NegativeLimit); // 1 - 3 = -2

        using var db = DbFor(_tenantA);
        db.LeaveRequests.Single(lr => lr.Id == requestId).Status.Should().Be(LeaveRequestStatus.Approved);

        var used = db.LeaveLedgerEntries.Single(l =>
            l.LeaveRequestId == requestId && l.EntryType == LedgerEntryType.Used);
        used.Amount.Should().Be(-3m);
        used.BalanceAfter.Should().Be(-NegativeLimit);
    }
}
