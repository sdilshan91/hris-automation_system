// ============================================================================
// DF-64-pg — the regularization → overtime-record path (US-ATT-004 / US-ATT-006) exercised on REAL
// postgres:17-alpine through the real approve funnel (RegularizationApprovalService.ApproveAsync →
// ApproveCoreAsync → ApplyToAttendanceLogAsync + the real OvertimeService.BuildAutoDetectedAsync).
//
// WHY POSTGRES (not InMemory): the DF-64 unit arms in RegularizationApprovalServiceTests use the EF
// InMemory provider, which IGNORES foreign keys — so it cannot prove the ONE behaviour that actually
// protects paid overtime: the FK
//     overtime_approval_history.overtime_record_id → overtime_records.id  ON DELETE CASCADE
// means that deleting an Approved (payroll-ready) auto-detected record would CASCADE-DELETE its
// immutable OvertimeApprovalHistory. The DF-64 guard (never supersede a terminal decision) is what
// prevents that cascade; only real Postgres, which enforces the FK + cascade, can prove the guard holds.
//
// Arms:
//   1. Approving a regularized OT day CREATES a payable-but-Pending OvertimeRecord (IsPayrollReady=false).
//   2. CRITICAL guard — a prior APPROVED (terminal / payroll-ready) auto-detected record is NOT
//      deleted/replaced, and its OvertimeApprovalHistory row survives (the cascade never fires).
//   3. A prior PENDING auto-detected record IS superseded — exactly one record for the day (replace,
//      not duplicate) — proven under real FK enforcement.
//
// FK parents (enforced on Postgres, ignored by InMemory): a User for fk_employees_users_user_id and a
// Department + JobTitle per employee. Harness mirrors AttendanceChronicLatenessPostgresTests /
// FinalSettlementPostgresTests (self-contained container + IAsyncLifetime, no shared base class).
// UseSnakeCaseNamingConvention() is NOT optional (omitting it makes MigrateAsync throw
// PendingModelChangesWarning). Timestamps are DateTimeKind.Utc (BUG-289/290: an Unspecified DateTime
// is rejected by a timestamptz column on real Postgres, though InMemory would accept it).
//
// Traceability: @TC-ATT-006-64 (the DF-64 OT-record path) against the real provider.
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

[Trait("TC", "TC-ATT-006-64")]
public sealed class RegularizationOvertimeRecordPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _reportUserId = Guid.NewGuid();

    private Guid _managerId;
    private Guid _reportId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db();
        await db.Database.MigrateAsync();
        await SeedActorsAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId { get; init; }
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
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }

    private AppDbContext Db()
    {
        var tc = new FixedTenantContext { TenantId = _tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(_managerUserId);   // the approving manager (GetCurrentEmployeeAsync resolves by UserId).

        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_postgres.GetConnectionString(), n =>
                {
                    n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    n.EnableRetryOnFailure(maxRetryCount: 3);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
                .Options,
            tc);
    }

    /// <summary>The approve funnel wired with a REAL OvertimeService + ShiftService over the SAME context.</summary>
    private RegularizationApprovalService ServiceWithOvertime(AppDbContext db)
    {
        var tc = new FixedTenantContext { TenantId = _tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(_managerUserId);
        return new RegularizationApprovalService(
            db, tc, cu,
            new ShiftService(db, tc, cu, NullLogger<ShiftService>.Instance),
            NullLogger<RegularizationApprovalService>.Instance,
            overtimeService: new OvertimeService(db, tc, cu, NullLogger<OvertimeService>.Instance));
    }

    // ── seeding ─────────────────────────────────────────────────────────

    private static DateTime At(DateOnly date, int hour)
        => date.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Utc);

    private static DateOnly Yesterday => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

    /// <summary>Seeds the manager + a direct report, with their FK-required User/Department/JobTitle parents.</summary>
    private async Task SeedActorsAsync()
    {
        _managerId = BaseEntity.NewUuidV7();
        _reportId = BaseEntity.NewUuidV7();

        await using var db = Db();
        // Postgres enforces fk_employees_users_user_id (InMemory does not) — seed the linked User rows.
        db.Users.Add(new User { Id = _managerUserId, Email = "manager@acme.test" });
        db.Users.Add(new User { Id = _reportUserId, Email = "report@acme.test" });

        db.Employees.Add(NewEmployee(db, _managerId, _managerUserId, "MGR", reportsTo: null));
        db.Employees.Add(NewEmployee(db, _reportId, _reportUserId, "RPT", reportsTo: _managerId));
        await db.SaveChangesAsync();
    }

    private static Employee NewEmployee(AppDbContext db, Guid id, Guid userId, string no, Guid? reportsTo)
    {
        // TenantId is stamped by TenantInterceptor on save (it fills any BaseEntity left at Guid.Empty).
        var dept = new Department { Id = BaseEntity.NewUuidV7(), Name = $"D{no}", Code = no };
        var title = new JobTitle { Id = BaseEntity.NewUuidV7(), TitleName = $"T{no}" };
        db.Departments.Add(dept);
        db.JobTitles.Add(title);
        return new Employee
        {
            Id = id, UserId = userId, EmployeeNo = no, FirstName = no, LastName = "W",
            Email = $"{no}@acme.test".ToLowerInvariant(), DateOfJoining = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DepartmentId = dept.Id, JobTitleId = title.Id, EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active, IsActive = true, ReportsToEmployeeId = reportsTo,
        };
    }

    private async Task SeedOvertimeSettingsAsync()
    {
        await using var db = Db();
        db.AttendanceSettings.Add(new AttendanceSettings
        {
            Id = BaseEntity.NewUuidV7(),
            StandardWorkMinutes = 480, OvertimeMinimumThresholdMinutes = 0, MaxDailyOvertimeMinutes = 600,
            MaxWeeklyOvertimeMinutes = 3000, AutoBreakThresholdMinutes = 360, AutoBreakMinutes = 60,
            RequireOvertimePreApproval = false,
            WeekdayOvertimeMultiplier = 1.5m, WeekendOvertimeMultiplier = 2m, HolidayOvertimeMultiplier = 2.5m,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedAutoClosedLogAsync(DateOnly date, int inHour, int outHour)
    {
        await using var db = Db();
        var id = BaseEntity.NewUuidV7();
        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = id, EmployeeId = _reportId,
            ClockIn = At(date, inHour), ClockOut = At(date, outHour),
            Status = "ANOMALY", Source = "SYSTEM",
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedAutoDetectedOtRecordAsync(
        Guid logId, DateOnly date, int minutes,
        string status = OvertimeStatus.Pending, bool payrollReady = false)
    {
        await using var db = Db();
        var id = BaseEntity.NewUuidV7();
        db.OvertimeRecords.Add(new OvertimeRecord
        {
            Id = id, EmployeeId = _reportId, AttendanceLogId = logId, Date = date,
            OvertimeMinutes = minutes, ApprovedMinutes = status == OvertimeStatus.Approved ? minutes : null,
            Multiplier = 1.5m, Type = OvertimeType.AutoDetected, Status = status, IsPayrollReady = payrollReady,
            Reason = "Auto-detected overtime on clock-out.", CalculationBasis = "seed",
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>Seeds the immutable approval-history row for an Approved record (the one the cascade would drop).</summary>
    private async Task<Guid> SeedOtApprovalHistoryAsync(Guid overtimeRecordId, int approvedMinutes)
    {
        await using var db = Db();
        var id = BaseEntity.NewUuidV7();
        db.OvertimeApprovalHistories.Add(new OvertimeApprovalHistory
        {
            Id = id, OvertimeRecordId = overtimeRecordId, ApproverEmployeeId = _managerId,
            ApprovalLevel = 1, Action = "APPROVED", ApprovedMinutes = approvedMinutes,
            Comment = "Approved OT", CalculationBasis = "seed", ActionedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedPendingRegAsync(DateOnly date, int requestedOutHour, Guid linkedLogId)
    {
        await using var db = Db();
        var id = BaseEntity.NewUuidV7();
        db.AttendanceRegularizations.Add(new AttendanceRegularization
        {
            Id = id, EmployeeId = _reportId, AttendanceLogId = linkedLogId, Date = date,
            RegularizationType = RegularizationType.MissedClockOut,
            RequestedClockOut = At(date, requestedOutHour),
            Reason = "Forgot to punch out on this working day", Status = RegularizationStatus.Pending,
        });
        await db.SaveChangesAsync();
        return id;
    }

    // ── arms ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Approve_RegularizedOtDay_CreatesPayablePendingRecord_OnPostgres()
    {
        var date = Yesterday;
        await SeedOvertimeSettingsAsync();
        // An auto-closed ANOMALY day (8:00 -> system 23:00) with NO overtime record — the DF-64 gap.
        var logId = await SeedAutoClosedLogAsync(date, inHour: 8, outHour: 23);
        // The employee regularizes the clock-out to 20:00 -> 8-20 = 720 gross, 60 break -> 660 net -> OT 180.
        var regId = await SeedPendingRegAsync(date, requestedOutHour: 20, linkedLogId: logId);

        await using (var db = Db())
            (await ServiceWithOvertime(db).ApproveAsync(regId, "Approved OT")).IsSuccess.Should().BeTrue();

        await using var verify = Db();
        var ot = (await verify.OvertimeRecords.AsNoTracking()
            .Where(o => o.AttendanceLogId == logId).ToListAsync()).Should().ContainSingle().Subject;
        ot.OvertimeMinutes.Should().Be(180, "660 net - 480 standard = 180 OT minutes");
        ot.Type.Should().Be(OvertimeType.AutoDetected);
        ot.Status.Should().Be(OvertimeStatus.Pending, "the record enters the NORMAL OT approval flow, not auto-approved");
        ot.IsPayrollReady.Should().BeFalse("Pending OT is not payroll-ready until separately approved");
    }

    [Fact]
    public async Task Approve_OnDayWithApprovedOt_PreservesApprovedRecordAndHistory_OnPostgres()
    {
        var date = Yesterday;
        await SeedOvertimeSettingsAsync();
        var logId = await SeedAutoClosedLogAsync(date, inHour: 8, outHour: 20);
        // A manager already APPROVED this day's OT (payroll-ready), with its immutable history row. On real
        // Postgres the overtime_approval_history -> overtime_records FK is ON DELETE CASCADE, so deleting the
        // record would drop the history too — the DF-64 guard must prevent both.
        var approvedId = await SeedAutoDetectedOtRecordAsync(logId, date, minutes: 120,
            status: OvertimeStatus.Approved, payrollReady: true);
        var historyId = await SeedOtApprovalHistoryAsync(approvedId, approvedMinutes: 120);
        // A LATER same-day regularization is approved (to 22:00 -> would recompute to MORE OT).
        var regId = await SeedPendingRegAsync(date, requestedOutHour: 22, linkedLogId: logId);

        await using (var db = Db())
            (await ServiceWithOvertime(db).ApproveAsync(regId, "Late correction")).IsSuccess.Should().BeTrue();

        await using var verify = Db();
        var records = await verify.OvertimeRecords.AsNoTracking()
            .Where(o => o.AttendanceLogId == logId).ToListAsync();
        records.Should().ContainSingle("the approved record is preserved, not replaced by a new pending one");
        var ot = records.Single();
        ot.Id.Should().Be(approvedId, "the SAME approved record survives the later time correction");
        ot.Status.Should().Be(OvertimeStatus.Approved, "an approved OT decision is not silently re-opened");
        ot.OvertimeMinutes.Should().Be(120, "the approved value is untouched");
        ot.IsPayrollReady.Should().BeTrue("the payroll-ready approved OT stands");

        // The FK-backed proof InMemory cannot give: the immutable history row is NOT cascade-deleted.
        (await verify.OvertimeApprovalHistories.AsNoTracking()
            .AnyAsync(h => h.Id == historyId && h.OvertimeRecordId == approvedId))
            .Should().BeTrue("preserving the record also preserves its immutable OvertimeApprovalHistory (no cascade delete)");
    }

    [Fact]
    public async Task Approve_OnDayWithPendingOt_SupersedesRecord_NoDuplicate_OnPostgres()
    {
        var date = Yesterday;
        await SeedOvertimeSettingsAsync();
        // A day with an EXISTING auto-detected PENDING OT record of 120 (a prior clock-out's value)...
        var logId = await SeedAutoClosedLogAsync(date, inHour: 8, outHour: 23);
        await SeedAutoDetectedOtRecordAsync(logId, date, minutes: 120);
        // ...is regularized to 8-20 -> 660 net -> OT 180. The stale 120 must be REPLACED, not duplicated.
        var regId = await SeedPendingRegAsync(date, requestedOutHour: 20, linkedLogId: logId);

        await using (var db = Db())
            (await ServiceWithOvertime(db).ApproveAsync(regId, "Corrected up")).IsSuccess.Should().BeTrue();

        await using var verify = Db();
        var ot = (await verify.OvertimeRecords.AsNoTracking()
            .Where(o => o.AttendanceLogId == logId && o.Type == OvertimeType.AutoDetected).ToListAsync())
            .Should().ContainSingle("the prior auto-detected record is replaced, not duplicated").Subject;
        ot.OvertimeMinutes.Should().Be(180, "the record carries the RECOMPUTED overtime, not the stale 120");
        ot.Status.Should().Be(OvertimeStatus.Pending, "a changed OT record re-enters the approval flow");
    }
}
