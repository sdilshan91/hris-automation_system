// ============================================================================
// Attendance missing-audit-write regression cluster (7 findings).
//
// The Attendance module's write operations mutate business state but — unlike the
// reference LeaveTypeService (which appends an `audit_logs` row inside the same
// SaveChanges via the `AuditLogs.Add` pattern) — historically wrote NO audit trail.
// Each test below drives a real Attendance write through the real service (composed
// DI pipeline + MediatR + ITenantContext global query filters), then queries the
// `audit_logs` table and asserts a structured audit row exists: correct action
// (substring), ResourceId == the mutated entity, tenant-scoped, actor-attributed.
//
// Findings covered (one clearly-named arm per finding; ISSUE-073 gets both decisions):
//   ISSUE-067  AttendanceService clock-in           -> Attendance.ClockedIn
//   ISSUE-069  AttendanceService clock-out          -> Attendance.ClockedOut
//   ISSUE-071  regularization submit                -> AttendanceRegularization.Submitted
//   ISSUE-073  regularization approve / reject      -> AttendanceRegularization.Approved / .Rejected
//   ISSUE-075  shift create/update/delete/assign/clone -> Shift.*
//   ISSUE-089  attendance period lock / unlock      -> AttendancePeriod.Locked / .Unlocked
//   ISSUE-093  scheduled-report-config create/update/delete -> ScheduledReport.*
//
// PRE-FIX: no service writes an audit row, so every assertion below fails (the row is
// absent). POST-FIX (the parallel agent adds the `AuditLogs.Add` calls): they pass.
//
// NOTE ON PROVIDER: audit rows are plain inserts with no PostgreSQL-specific behaviour,
// so InMemory is sufficient and matches the rest of the Attendance integration suite
// (the verify gate runs `dotnet test` with no PostgreSQL / Docker bound). `audit_logs`
// has NO global query filter (see AuditLogConfiguration), so any tenant context reads
// every row — which lets each arm assert the written row's TenantId directly.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.Commands;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class AttendanceAuditWriteTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private readonly Guid _userA = Guid.NewGuid();          // acts as the employee (clock-in/out, submit)
    private readonly Guid _managerUserA = Guid.NewGuid();   // acts as the approving manager

    private Guid _empA;         // linked to _userA
    private Guid _managerA;     // linked to _managerUserA
    private Guid _reportA;      // reports to _managerA (subject of the regularization)

    public AttendanceAuditWriteTests() => SeedBase();

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

    private sealed class InMemoryExportStorage : IReportExportStorage
    {
        public Task<string> SaveAsync(Guid tenantId, Guid reportId, string fileName,
            string contentType, byte[] content, CancellationToken cancellationToken = default)
            => Task.FromResult($"mem://{tenantId}/{reportId}/{fileName}");
    }

    /// <summary>
    /// Composes the full Attendance service graph so a single pipeline can drive every audited op.
    /// The acting user is authenticated (so audit rows are actor-attributed) and holds ViewAll so
    /// dashboard/scheduled-report operations are permitted.
    /// </summary>
    private (IMediator Mediator, IServiceProvider Provider) BuildPipeline(Guid tenantId, Guid userId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.Email.Returns("actor@test.com");
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Permissions.Returns(new[] { PermissionCatalog.Attendance.ViewAll });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddSingleton<IReportExportStorage, InMemoryExportStorage>();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IOvertimeService, OvertimeService>();
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddScoped<IAttendanceSummaryService, AttendanceSummaryService>();
        services.AddScoped<IRegularizationApprovalService, RegularizationApprovalService>();
        services.AddScoped<IAttendancePayrollService, AttendancePayrollService>();
        services.AddScoped<IAttendanceDashboardService, AttendanceDashboardService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ClockInCommand).Assembly));

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMediator>(), provider);
    }

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    private void SeedBase()
    {
        using var db = Db(_tenantA);

        _empA = Guid.NewGuid();
        _managerA = Guid.NewGuid();
        _reportA = Guid.NewGuid();

        db.Employees.AddRange(
            Emp(_empA, _tenantA, _userA, "A1", null),
            Emp(_managerA, _tenantA, _managerUserA, "MGRA", null),
            Emp(_reportA, _tenantA, Guid.NewGuid(), "REPA", _managerA));
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

    // ── audit-row helpers ─────────────────────────────────────────────────────

    /// <summary>Every audit_logs row in the store (the table has no tenant query filter).</summary>
    private List<AuditLog> AuditRows()
    {
        using var db = Db(_tenantA);
        return db.AuditLogs.AsNoTracking().ToList();
    }

    private static string ActionText(AuditLog r) => $"{r.Action}|{r.EventType}";

    /// <summary>
    /// Asserts exactly the audit contract each finding demands: a row for <paramref name="resourceId"/>
    /// whose action contains <paramref name="actionSubstring"/>, stamped with the acting tenant and actor.
    /// </summary>
    private AuditLog AssertAuditRow(string actionSubstring, Guid resourceId, Guid tenantId, Guid actorUserId)
    {
        var rows = AuditRows();
        var match = rows.FirstOrDefault(r =>
            r.ResourceId == resourceId.ToString() &&
            ActionText(r).Contains(actionSubstring, StringComparison.OrdinalIgnoreCase));

        match.Should().NotBeNull(
            "an audit row with action ~'{0}' for resource {1} must be written (found: {2})",
            actionSubstring, resourceId,
            rows.Count == 0 ? "<none>" : string.Join(", ", rows.Select(ActionText)));

        match!.TenantId.Should().Be(tenantId, "the audit row must be tenant-scoped");
        match.UserId.Should().Be(actorUserId, "the audit row must be attributed to the acting user");
        return match;
    }

    private static ShiftRequest SingleShift(string name) => new()
    {
        Name = name, Type = ShiftType.Single, StartTime = "09:00", EndTime = "17:00",
        BreakDurationMinutes = 60, GracePeriodMinutes = 10, WorkingDays = new[] { 1, 2, 3, 4, 5 },
    };

    // ════════════════════════════════════════════════════════════════════════
    //  ISSUE-067 — clock-in writes Attendance.ClockedIn
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ClockIn_WritesAuditRow_ISSUE067()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(
            new ClockInCommand(null, null, null, "WEB", "10.0.0.1", "RegressionTest/1.0", null));
        result.IsSuccess.Should().BeTrue();

        // The mutated entity is the newly-created attendance log.
        using var db = Db(_tenantA);
        var log = db.AttendanceLogs.Single(a => a.EmployeeId == _empA);

        AssertAuditRow("ClockedIn", log.Id, _tenantA, _userA);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ISSUE-069 — clock-out writes Attendance.ClockedOut
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ClockOut_WritesAuditRow_ISSUE069()
    {
        // Seed an open clock-in (480 min ago) for the acting employee, then clock out.
        var logId = BaseEntity.NewUuidV7();
        using (var seed = Db(_tenantA))
        {
            seed.AttendanceLogs.Add(new AttendanceLog
            {
                Id = logId, TenantId = _tenantA, EmployeeId = _empA,
                ClockIn = DateTime.UtcNow.AddMinutes(-480), Source = "WEB",
            });
            seed.SaveChanges();
        }

        var (mediator, _) = BuildPipeline(_tenantA, _userA);
        var result = await mediator.Send(new ClockOutCommand(null, null, "10.0.0.1", "RegressionTest/1.0"));
        result.IsSuccess.Should().BeTrue();

        AssertAuditRow("ClockedOut", logId, _tenantA, _userA);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ISSUE-071 — regularization submit writes AttendanceRegularization.Submitted
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SubmitRegularization_WritesAuditRow_ISSUE071()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

        var result = await mediator.Send(new SubmitRegularizationCommand(new SubmitRegularizationRequest
        {
            Date = date,
            RegularizationType = RegularizationType.MissedBoth,
            RequestedClockIn = "09:00",
            RequestedClockOut = "17:00",
            Reason = "Forgot to clock in and out on this day",
        }));
        result.IsSuccess.Should().BeTrue();

        // The mutated entity is the created regularization request.
        AssertAuditRow("Submitted", result.Value!.Id, _tenantA, _userA);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ISSUE-073 — approve / reject write the correct distinct action
    // ════════════════════════════════════════════════════════════════════════

    private Guid SeedPendingRegularization(DateOnly date)
    {
        using var db = Db(_tenantA);
        var id = BaseEntity.NewUuidV7();
        db.AttendanceRegularizations.Add(new AttendanceRegularization
        {
            Id = id, TenantId = _tenantA, EmployeeId = _reportA, Date = date,
            RegularizationType = RegularizationType.MissedBoth,
            RequestedClockIn = date.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc),
            RequestedClockOut = date.ToDateTime(new TimeOnly(17, 0), DateTimeKind.Utc),
            Reason = "Forgot to clock in and out on this day",
            Status = RegularizationStatus.Pending,
        });
        db.SaveChanges();
        return id;
    }

    [Fact]
    public async Task ApproveRegularization_WritesApprovedAuditRow_ISSUE073()
    {
        var regId = SeedPendingRegularization(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
        var (mediator, _) = BuildPipeline(_tenantA, _managerUserA);

        var result = await mediator.Send(new ApproveRegularizationCommand(regId, "ok"));
        result.IsSuccess.Should().BeTrue();

        AssertAuditRow("Approved", regId, _tenantA, _managerUserA);
    }

    [Fact]
    public async Task RejectRegularization_WritesRejectedAuditRow_ISSUE073()
    {
        var regId = SeedPendingRegularization(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
        var (mediator, _) = BuildPipeline(_tenantA, _managerUserA);

        var result = await mediator.Send(
            new RejectRegularizationCommand(regId, "The requested times conflict with badge data"));
        result.IsSuccess.Should().BeTrue();

        // Same shared code path as approve, but must record the distinct 'Rejected' action.
        AssertAuditRow("Rejected", regId, _tenantA, _managerUserA);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ISSUE-075 — shift create / update / delete / assign / clone write Shift.*
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateShift_WritesAuditRow_ISSUE075()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);

        var shift = (await mediator.Send(new CreateShiftCommand(SingleShift("Morning")))).Value!;

        AssertAuditRow("Shift", shift.Id, _tenantA, _userA);
    }

    [Fact]
    public async Task UpdateShift_WritesAuditRowWithBeforeAfter_ISSUE075()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);
        var shift = (await mediator.Send(new CreateShiftCommand(SingleShift("Ops")))).Value!;

        var changed = SingleShift("Ops") with { GracePeriodMinutes = 25 };
        var updated = await mediator.Send(new UpdateShiftCommand(shift.Id, changed));
        updated.IsSuccess.Should().BeTrue();

        // An update must record a before/after snapshot that actually differs.
        var row = AssertAuditRow("Shift", shift.Id, _tenantA, _userA);
        var updateRow = AuditRows().First(r =>
            r.ResourceId == shift.Id.ToString() &&
            ActionText(r).Contains("Updated", StringComparison.OrdinalIgnoreCase));
        updateRow.Before.Should().NotBeNullOrEmpty("an update audit must snapshot the prior state");
        updateRow.After.Should().NotBeNullOrEmpty("an update audit must snapshot the new state");
        updateRow.Before.Should().NotBe(updateRow.After, "before and after must differ for a real change");
    }

    [Fact]
    public async Task DeleteShift_WritesAuditRow_ISSUE075()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);
        var shift = (await mediator.Send(new CreateShiftCommand(SingleShift("Temp")))).Value!;

        var delete = await mediator.Send(new DeleteShiftCommand(shift.Id));
        delete.IsSuccess.Should().BeTrue();

        var row = AssertAuditRow("Shift", shift.Id, _tenantA, _userA);
        AuditRows().Should().Contain(r =>
            r.ResourceId == shift.Id.ToString() &&
            ActionText(r).Contains("Deleted", StringComparison.OrdinalIgnoreCase),
            "a delete must record a Shift.Deleted action");
    }

    [Fact]
    public async Task AssignShift_WritesAuditRow_ISSUE075()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);
        var shift = (await mediator.Send(new CreateShiftCommand(SingleShift("Assignable")))).Value!;

        var assign = await mediator.Send(new AssignShiftCommand(
            shift.Id, new[] { _empA }, DateOnly.FromDateTime(DateTime.UtcNow)));
        assign.Value!.AssignedCount.Should().Be(1);

        AuditRows().Should().Contain(r =>
            r.ResourceId == shift.Id.ToString() &&
            ActionText(r).Contains("Assign", StringComparison.OrdinalIgnoreCase) &&
            r.TenantId == _tenantA && r.UserId == _userA,
            "assigning employees to a shift must record a Shift.Assigned audit row");
    }

    [Fact]
    public async Task CloneShift_WritesAuditRow_ISSUE075()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);
        var shift = (await mediator.Send(new CreateShiftCommand(SingleShift("Original")))).Value!;

        var clone = await mediator.Send(new CloneShiftCommand(shift.Id));
        clone.IsSuccess.Should().BeTrue();

        // The clone produces a NEW shift; its creation must itself be audited.
        AssertAuditRow("Clon", clone.Value!.Id, _tenantA, _userA);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ISSUE-089 — period lock / unlock write AttendancePeriod.Locked / .Unlocked
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LockPeriod_WritesAuditRow_ISSUE089()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);
        var m = DateOnly.FromDateTime(DateTime.UtcNow);

        var locked = await mediator.Send(new LockPeriodCommand(
            new DateOnly(m.Year, m.Month, 1), new DateOnly(m.Year, m.Month, 28)));
        locked.IsSuccess.Should().BeTrue();

        AssertAuditRow("Lock", locked.Value!.Id, _tenantA, _userA);
    }

    [Fact]
    public async Task UnlockPeriod_WritesAuditRow_ISSUE089()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);
        var m = DateOnly.FromDateTime(DateTime.UtcNow);
        var locked = await mediator.Send(new LockPeriodCommand(
            new DateOnly(m.Year, m.Month, 1), new DateOnly(m.Year, m.Month, 28)));

        var unlocked = await mediator.Send(new UnlockPeriodCommand(locked.Value!.Id));
        unlocked.IsSuccess.Should().BeTrue();

        // Unlock must record the distinct 'Unlocked' action on the same period-lock entity.
        var row = AuditRows().FirstOrDefault(r =>
            r.ResourceId == locked.Value!.Id.ToString() &&
            ActionText(r).Contains("Unlock", StringComparison.OrdinalIgnoreCase));
        row.Should().NotBeNull("unlocking a period must write an AttendancePeriod.Unlocked audit row");
        row!.TenantId.Should().Be(_tenantA);
        row.UserId.Should().Be(_userA);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ISSUE-093 — scheduled-report-config create / update / delete write ScheduledReport.*
    // ════════════════════════════════════════════════════════════════════════

    private static ScheduledReportConfigDto ReportDto() => new()
    {
        ReportType = "CUSTOM",
        Frequency = "WEEKLY",
        Format = "XLSX",
        DeliveryTime = "08:30",
        Recipients = new[] { Guid.NewGuid() },
        IsActive = true,
    };

    [Fact]
    public async Task CreateScheduledReport_WritesAuditRow_ISSUE093()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);

        var created = await mediator.Send(new CreateScheduledReportCommand(ReportDto()));
        created.IsSuccess.Should().BeTrue();

        AssertAuditRow("ScheduledReport", created.Value!.Id!.Value, _tenantA, _userA);
    }

    [Fact]
    public async Task UpdateScheduledReport_WritesAuditRow_ISSUE093()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);
        var created = (await mediator.Send(new CreateScheduledReportCommand(ReportDto()))).Value!;

        var updated = await mediator.Send(new UpdateScheduledReportCommand(
            created.Id!.Value, ReportDto() with { Frequency = "MONTHLY", IsActive = false }));
        updated.IsSuccess.Should().BeTrue();

        AuditRows().Should().Contain(r =>
            r.ResourceId == created.Id!.Value.ToString() &&
            ActionText(r).Contains("Updated", StringComparison.OrdinalIgnoreCase) &&
            r.TenantId == _tenantA && r.UserId == _userA,
            "updating a scheduled-report config must record a ScheduledReport.Updated audit row");
    }

    [Fact]
    public async Task DeleteScheduledReport_WritesAuditRow_ISSUE093()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);
        var created = (await mediator.Send(new CreateScheduledReportCommand(ReportDto()))).Value!;

        var deleted = await mediator.Send(new DeleteScheduledReportCommand(created.Id!.Value));
        deleted.IsSuccess.Should().BeTrue();

        AuditRows().Should().Contain(r =>
            r.ResourceId == created.Id!.Value.ToString() &&
            ActionText(r).Contains("Deleted", StringComparison.OrdinalIgnoreCase) &&
            r.TenantId == _tenantA && r.UserId == _userA,
            "deleting a scheduled-report config must record a ScheduledReport.Deleted audit row");
    }
}
