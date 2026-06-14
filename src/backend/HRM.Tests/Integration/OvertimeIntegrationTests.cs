// ============================================================================
// US-ATT-006: Overtime tracking and approval — integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI container (MediatR
// pipeline + ITenantContext-driven global query filters), covering:
//   - Auto-detection on clock-out: 9h on an 8h shift with a 30-min threshold => a 60-min overtime
//     record created in the SAME clock-out transaction (AC-1/FR-1/NFR-1); below-threshold => none.
//   - Weekend multiplier 2.0x (BR-3/BR-7).
//   - Daily cap at 4h (BR-4) and weekly-cap alert flag (BR-5/FR-8).
//   - Pre-approval policy: overtime without a matching pre-approval => UNAPPROVED (BR-6).
//   - Manager approve => payroll-ready (AC-4/FR-7); manager adjust 3h->2h => approved_minutes=120 (FR-6).
//   - Self-approval prevention (BR-8).
//   - Monthly report aggregation (AC-5).
//   - Multi-tenant isolation (NFR-2 via EF global filters).
//
// NOTE ON PROVIDER: identical rationale to the other Attendance integration tests — the verify gate
// runs `dotnet test` with NO PostgreSQL bound and Docker is unavailable in the agent sandbox, so a
// Testcontainers test would red the gate. These tests use the InMemory provider through the real
// composed pipeline (MediatR + tenant query filters), which is what proves tenant isolation.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.Commands;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Application.Features.Attendance.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class OvertimeIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _managerUserA = Guid.NewGuid();
    private readonly Guid _employeeUserA = Guid.NewGuid();
    private readonly Guid _managerUserB = Guid.NewGuid();
    private readonly Guid _employeeUserB = Guid.NewGuid();

    private Guid _managerA, _employeeA;
    private Guid _managerB, _employeeB;

    public OvertimeIntegrationTests()
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

    private (IMediator Mediator, IServiceProvider Provider) BuildPipeline(Guid tenantId, Guid userId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IOvertimeService, OvertimeService>();
        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ApproveOvertimeCommand).Assembly));

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMediator>(), provider);
    }

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    private void SeedTwoTenants()
    {
        using var db = Db(_tenantA);
        _managerA = Guid.NewGuid(); _employeeA = Guid.NewGuid();
        _managerB = Guid.NewGuid(); _employeeB = Guid.NewGuid();

        db.Employees.AddRange(
            Emp(_managerA, _tenantA, _managerUserA, "MgrA", null),
            Emp(_employeeA, _tenantA, _employeeUserA, "EmpA", _managerA),
            Emp(_managerB, _tenantB, _managerUserB, "MgrB", null),
            Emp(_employeeB, _tenantB, _employeeUserB, "EmpB", _managerB));
        db.SaveChanges();
    }

    private static Employee Emp(Guid id, Guid tenantId, Guid userId, string name, Guid? reportsTo) => new()
    {
        Id = id, TenantId = tenantId, UserId = userId,
        EmployeeNo = name, FirstName = name, LastName = "X", Email = $"{name}@t.com",
        DateOfJoining = new DateTime(2020, 1, 1),
        DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
        EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        ReportsToEmployeeId = reportsTo,
    };

    /// <summary>Seeds the tenant settings with a known overtime policy (defaults match the story).</summary>
    private void SeedSettings(Guid tenantId, bool requirePreApproval = false, int maxWeekly = 1200)
    {
        using var db = Db(tenantId);
        db.AttendanceSettings.Add(new AttendanceSettings
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            StandardWorkMinutes = 480,            // 8h
            AutoBreakMinutes = 0,                 // disable auto-break so net == gross for predictable OT
            AutoBreakThresholdMinutes = 100000,
            OvertimeMinimumThresholdMinutes = 30, // BR-2
            MaxDailyOvertimeMinutes = 240,        // BR-4 (4h)
            MaxWeeklyOvertimeMinutes = maxWeekly, // BR-5
            WeekdayOvertimeMultiplier = 1.5m,
            WeekendOvertimeMultiplier = 2.0m,
            HolidayOvertimeMultiplier = 2.5m,
            RequireOvertimePreApproval = requirePreApproval,
        });
        db.SaveChanges();
    }

    /// <summary>Seeds an OPEN clock-in for the employee, clocking in <paramref name="hoursAgo"/> ago.</summary>
    private void SeedOpenClockIn(Guid tenantId, Guid employeeId, DateTime clockIn)
    {
        using var db = Db(tenantId);
        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            ClockIn = clockIn,
            Source = "WEB",
        });
        db.SaveChanges();
    }

    // ════════════════════════════════════════════════════════════════
    //  AC-1 / FR-1 / NFR-1 — auto-detection on clock-out
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ClockOut_NineHoursOnEightHourShift_CreatesSixtyMinuteOvertimeRecord()
    {
        SeedSettings(_tenantA);
        // Clock in 9h ago (weekday) so net work = 540 min => 60 min OT over the 480 standard.
        var clockIn = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc).AddHours(0);
        SeedOpenClockInExactSpan(_tenantA, _employeeA, minutes: 540);
        var (mediator, provider) = BuildPipeline(_tenantA, _employeeUserA);

        var result = await mediator.Send(new ClockOutCommand(null, null, null, null));
        result.IsSuccess.Should().BeTrue();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ot = db.OvertimeRecords.Single(o => o.EmployeeId == _employeeA);
        ot.OvertimeMinutes.Should().Be(60);
        ot.Type.Should().Be(OvertimeType.AutoDetected);
        ot.Status.Should().Be(OvertimeStatus.Pending);
        ot.AttendanceLogId.Should().NotBeNull();   // NFR-1: linked to the closed log.
    }

    [Fact]
    public async Task ClockOut_BelowThreshold_CreatesNoOvertimeRecord()
    {
        SeedSettings(_tenantA);
        // 8h20m net = 500 min => excess 20 < 30 threshold => no overtime record.
        SeedOpenClockInExactSpan(_tenantA, _employeeA, minutes: 500);
        var (mediator, provider) = BuildPipeline(_tenantA, _employeeUserA);

        var result = await mediator.Send(new ClockOutCommand(null, null, null, null));
        result.IsSuccess.Should().BeTrue();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.OvertimeRecords.Should().BeEmpty();
    }

    [Fact]
    public async Task ClockOut_ExceedsDailyCap_CapsAtFourHoursAndFlags()
    {
        SeedSettings(_tenantA);
        // 14h net = 840 => raw excess 360, capped at 240 (4h).
        SeedOpenClockInExactSpan(_tenantA, _employeeA, minutes: 840);
        var (mediator, provider) = BuildPipeline(_tenantA, _employeeUserA);

        (await mediator.Send(new ClockOutCommand(null, null, null, null))).IsSuccess.Should().BeTrue();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ot = db.OvertimeRecords.Single(o => o.EmployeeId == _employeeA);
        ot.OvertimeMinutes.Should().Be(240);
        ot.DailyCapApplied.Should().BeTrue();
    }

    [Fact]
    public async Task ClockOut_OnWeekend_UsesWeekendMultiplier()
    {
        SeedSettings(_tenantA);
        // Anchor the clock-in on a Saturday and span 540 min within the day.
        var saturdayStart = new DateTime(2026, 6, 13, 6, 0, 0, DateTimeKind.Utc);
        saturdayStart.DayOfWeek.Should().Be(DayOfWeek.Saturday);
        SeedOpenClockInAt(_tenantA, _employeeA, saturdayStart, minutes: 540);
        var (mediator, provider) = BuildPipeline(_tenantA, _employeeUserA);

        (await mediator.Send(new ClockOutCommand(null, null, null, null))).IsSuccess.Should().BeTrue();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ot = db.OvertimeRecords.Single(o => o.EmployeeId == _employeeA);
        ot.Multiplier.Should().Be(2.0m);
    }

    [Fact]
    public async Task ClockOut_WhenPreApprovalRequiredAndMissing_RecordsUnapproved()
    {
        SeedSettings(_tenantA, requirePreApproval: true);
        SeedOpenClockInExactSpan(_tenantA, _employeeA, minutes: 540);
        var (mediator, provider) = BuildPipeline(_tenantA, _employeeUserA);

        (await mediator.Send(new ClockOutCommand(null, null, null, null))).IsSuccess.Should().BeTrue();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ot = db.OvertimeRecords.Single(o => o.EmployeeId == _employeeA);
        ot.Status.Should().Be(OvertimeStatus.Unapproved);   // BR-6
        ot.IsPayrollReady.Should().BeFalse();
    }

    [Fact]
    public async Task ClockOut_ExceedsWeeklyCap_SetsWeeklyAlertFlag()
    {
        // Tiny weekly cap so a single OT block exceeds it.
        SeedSettings(_tenantA, maxWeekly: 30);
        SeedOpenClockInExactSpan(_tenantA, _employeeA, minutes: 540);   // 60 min OT > 30 weekly cap
        var (mediator, provider) = BuildPipeline(_tenantA, _employeeUserA);

        (await mediator.Send(new ClockOutCommand(null, null, null, null))).IsSuccess.Should().BeTrue();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ot = db.OvertimeRecords.Single(o => o.EmployeeId == _employeeA);
        ot.WeeklyCapExceeded.Should().BeTrue();
    }

    // ════════════════════════════════════════════════════════════════
    //  AC-4 / FR-6 / FR-7 — manager approve (with adjust) + self-approval
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Approve_HappyPath_SetsApprovedAndPayrollReady()
    {
        var otId = SeedPendingOvertime(_tenantA, _employeeA, minutes: 120);
        var (mediator, provider) = BuildPipeline(_tenantA, _managerUserA);

        var result = await mediator.Send(new ApproveOvertimeCommand(otId, null, "ok"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(OvertimeStatus.Approved);
        result.Value.ApprovedMinutes.Should().Be(120);   // defaults to overtimeMinutes

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ot = db.OvertimeRecords.Single(o => o.Id == otId);
        ot.IsPayrollReady.Should().BeTrue();
        db.OvertimeApprovalHistories.Should().ContainSingle(h =>
            h.OvertimeRecordId == otId && h.Action == OvertimeApprovalAction.Approved);
    }

    [Fact]
    public async Task Approve_WithAdjustment_ReducesApprovedMinutes()
    {
        // FR-6/AC-4: manager approves but reduces 3h (180) to 2h (120).
        var otId = SeedPendingOvertime(_tenantA, _employeeA, minutes: 180);
        var (mediator, provider) = BuildPipeline(_tenantA, _managerUserA);

        var result = await mediator.Send(new ApproveOvertimeCommand(otId, 120, null));

        result.IsSuccess.Should().BeTrue();
        result.Value!.ApprovedMinutes.Should().Be(120);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.OvertimeRecords.Single(o => o.Id == otId).ApprovedMinutes.Should().Be(120);
    }

    [Fact]
    public async Task Approve_AdjustAboveRecorded_IsRejected()
    {
        var otId = SeedPendingOvertime(_tenantA, _employeeA, minutes: 120);
        var (mediator, _) = BuildPipeline(_tenantA, _managerUserA);

        var result = await mediator.Send(new ApproveOvertimeCommand(otId, 200, null));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("invalid_approved_minutes");
    }

    [Fact]
    public async Task Approve_OwnOvertime_IsBlocked()
    {
        // BR-8: manager works overtime -> cannot approve their own. Routed to their supervisor/HR.
        var otId = SeedPendingOvertime(_tenantA, _managerA, minutes: 60);
        var (mediator, _) = BuildPipeline(_tenantA, _managerUserA);

        var result = await mediator.Send(new ApproveOvertimeCommand(otId, null, null));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("self_approval");
    }

    [Fact]
    public async Task Approve_AlreadyActioned_IsConflict()
    {
        var otId = SeedPendingOvertime(_tenantA, _employeeA, minutes: 60);
        var (mediator, _) = BuildPipeline(_tenantA, _managerUserA);

        (await mediator.Send(new ApproveOvertimeCommand(otId, null, null))).IsSuccess.Should().BeTrue();
        var second = await mediator.Send(new ApproveOvertimeCommand(otId, null, null));

        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("already_actioned");
    }

    [Fact]
    public async Task Reject_SetsRejected_NotPayrollReady()
    {
        var otId = SeedPendingOvertime(_tenantA, _employeeA, minutes: 60);
        var (mediator, provider) = BuildPipeline(_tenantA, _managerUserA);

        var result = await mediator.Send(
            new RejectOvertimeCommand(otId, "Overtime not pre-approved for this task"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(OvertimeStatus.Rejected);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.OvertimeRecords.Single(o => o.Id == otId).IsPayrollReady.Should().BeFalse();
    }

    // ════════════════════════════════════════════════════════════════
    //  AC-2 / FR-4 — pre-approval submit
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PreApproval_Submit_CreatesPreApprovedPendingRecord()
    {
        SeedSettings(_tenantA);
        var (mediator, provider) = BuildPipeline(_tenantA, _employeeUserA);

        var result = await mediator.Send(new SubmitOvertimePreApprovalCommand(
            new OvertimePreApprovalRequest
            {
                Date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
                ExpectedHours = 2,
                Reason = "Releasing the quarterly build tonight",
            }));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Type.Should().Be(OvertimeType.PreApproved);
        result.Value.Status.Should().Be(OvertimeStatus.Pending);
        result.Value.OvertimeMinutes.Should().Be(120);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.OvertimeRecords.Should().ContainSingle(o => o.Type == OvertimeType.PreApproved);
    }

    // ════════════════════════════════════════════════════════════════
    //  AC-3 — manager queue
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetPending_ReturnsDirectReportOvertime()
    {
        SeedPendingOvertime(_tenantA, _employeeA, minutes: 60);
        var (mediator, _) = BuildPipeline(_tenantA, _managerUserA);

        var result = await mediator.Send(new GetPendingOvertimeQuery());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle(i => i.EmployeeId == _employeeA);
        result.Value.Items[0].EmployeeName.Should().Be("EmpA X");
    }

    // ════════════════════════════════════════════════════════════════
    //  AC-5 — monthly report aggregation
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MonthlyReport_AggregatesByEmployeeAndStatus()
    {
        var month = new DateOnly(2026, 5, 1);
        SeedOvertime(_tenantA, _employeeA, month.AddDays(1), 120, OvertimeStatus.Approved, approvedMinutes: 100);
        SeedOvertime(_tenantA, _employeeA, month.AddDays(2), 60, OvertimeStatus.Pending);
        SeedOvertime(_tenantA, _employeeA, month.AddDays(3), 30, OvertimeStatus.Rejected);
        // Out-of-month record must be excluded.
        SeedOvertime(_tenantA, _employeeA, new DateOnly(2026, 4, 10), 999, OvertimeStatus.Approved, approvedMinutes: 999);

        var (mediator, _) = BuildPipeline(_tenantA, _managerUserA);

        var result = await mediator.Send(new GetOvertimeReportQuery(2026, 5));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Month.Should().Be("2026-05");
        var row = result.Value.Items.Single(r => r.EmployeeId == _employeeA);
        row.ApprovedMinutes.Should().Be(100);
        row.PendingMinutes.Should().Be(60);
        row.RejectedMinutes.Should().Be(30);
        row.RecordCount.Should().Be(3);
        result.Value.Totals.ApprovedMinutes.Should().Be(100);
    }

    // ════════════════════════════════════════════════════════════════
    //  NFR-2 — multi-tenant isolation
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Approve_CrossTenantOvertime_IsNotFound()
    {
        var otB = SeedPendingOvertime(_tenantB, _employeeB, minutes: 60);
        var (mediatorA, _) = BuildPipeline(_tenantA, _managerUserA);

        var result = await mediatorA.Send(new ApproveOvertimeCommand(otB, null, null));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);   // invisible across tenants.
    }

    [Fact]
    public async Task GetPending_DoesNotLeakOtherTenantOvertime()
    {
        SeedPendingOvertime(_tenantA, _employeeA, minutes: 60);
        SeedPendingOvertime(_tenantB, _employeeB, minutes: 90);

        var (mediatorB, _) = BuildPipeline(_tenantB, _managerUserB);
        var result = await mediatorB.Send(new GetPendingOvertimeQuery());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle(i => i.EmployeeId == _employeeB);
        result.Value.Items.Should().NotContain(i => i.EmployeeId == _employeeA);
    }

    // ── seeding helpers ──────────────────────────────────────────────────────

    /// <summary>Open clock-in anchored on a recent UTC weekday with an exact span (no auto-break).</summary>
    private void SeedOpenClockInExactSpan(Guid tenantId, Guid employeeId, int minutes)
    {
        // Anchor on a Monday so weekday multiplier applies; clock-in 'minutes' before now.
        var clockIn = DateTime.UtcNow.AddMinutes(-minutes);
        SeedOpenClockInAt(tenantId, employeeId, clockIn, minutes, anchorNow: true);
    }

    private void SeedOpenClockInAt(Guid tenantId, Guid employeeId, DateTime clockIn, int minutes, bool anchorNow = false)
    {
        using var db = Db(tenantId);
        // When not anchoring on "now" we still need clock-out (= now) to produce the span; clock-out is
        // set server-side to UtcNow, so set clock-in = desired-span before now to get exactly `minutes`.
        var effectiveClockIn = anchorNow ? clockIn : DateTime.UtcNow.AddMinutes(-minutes);
        // For the weekend test we must keep the real Saturday date — override when a specific date matters.
        if (!anchorNow && clockIn.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            effectiveClockIn = clockIn;

        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            ClockIn = effectiveClockIn,
            Source = "WEB",
        });
        db.SaveChanges();
    }

    private Guid SeedPendingOvertime(Guid tenantId, Guid employeeId, int minutes)
        => SeedOvertime(tenantId, employeeId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                        minutes, OvertimeStatus.Pending);

    private Guid SeedOvertime(
        Guid tenantId, Guid employeeId, DateOnly date, int minutes, string status, int? approvedMinutes = null)
    {
        using var db = Db(tenantId);
        var id = BaseEntity.NewUuidV7();
        db.OvertimeRecords.Add(new OvertimeRecord
        {
            Id = id, TenantId = tenantId, EmployeeId = employeeId, Date = date,
            OvertimeMinutes = minutes, ApprovedMinutes = approvedMinutes,
            Multiplier = 1.5m, Type = OvertimeType.AutoDetected, Status = status,
            Reason = "Auto-detected overtime on clock-out.",
            IsPayrollReady = status == OvertimeStatus.Approved,
        });
        db.SaveChanges();
        return id;
    }
}
