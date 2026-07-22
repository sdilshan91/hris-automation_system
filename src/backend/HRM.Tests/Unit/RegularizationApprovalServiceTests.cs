// ============================================================================
// US-ATT-004: Manager approve/reject of attendance regularization service unit tests.
// Covers: approve updates/creates attendance_log + recalculates via AttendanceCalculator (AC-1/FR-2),
// approve writes an immutable history row (FR-6/NFR-4), reject stores reason + leaves log untouched
// (AC-2/FR-3), reject with a too-short reason fails (BR-1), authorization denial for a non-team
// employee with the EXACT message (FR-7/AC-5), self-approval prevention (BR-6), immutability of an
// already-actioned request (BR-3), payroll-lock re-check at approval time (BR-5), the pending queue
// (FR-1/AC-3), and bulk approve with mixed per-item outcomes (BR-7). Uses the EF Core InMemory
// provider through the service (mirrors AttendanceRegularizationServiceTests / LeaveApprovalServiceTests).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class RegularizationApprovalServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<RegularizationApprovalService> _logger;

    private Guid _managerId;
    private Guid _reportId;       // a direct report of the manager
    private Guid _strangerId;     // an employee NOT reporting to the manager

    public RegularizationApprovalServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_managerUserId);

        _logger = Substitute.For<ILogger<RegularizationApprovalService>>();

        SeedOrg();
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private RegularizationApprovalService Service()
    {
        var db = Db();
        return new(db, _tenantContext, _currentUser, ShiftSvc(db), _logger);
    }

    private ShiftService ShiftSvc(AppDbContext db)
        => new(db, _tenantContext, _currentUser, Substitute.For<ILogger<ShiftService>>());

    /// <summary>DF-64: a service wired with a REAL OvertimeService sharing the SAME db context.</summary>
    private RegularizationApprovalService ServiceWithOvertime(AppDbContext db)
        => new(db, _tenantContext, _currentUser, ShiftSvc(db), _logger,
            overtimeService: new OvertimeService(db, _tenantContext, _currentUser,
                Substitute.For<ILogger<OvertimeService>>()));

    /// <summary>DF-64: seeds a tenant AttendanceSettings row with deterministic overtime knobs.</summary>
    private void SeedOvertimeSettings()
    {
        using var db = Db();
        db.AttendanceSettings.Add(new AttendanceSettings
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId,
            StandardWorkMinutes = 480, OvertimeMinimumThresholdMinutes = 0, MaxDailyOvertimeMinutes = 600,
            MaxWeeklyOvertimeMinutes = 3000, AutoBreakThresholdMinutes = 360, AutoBreakMinutes = 60,
            RequireOvertimePreApproval = false,
            WeekdayOvertimeMultiplier = 1.5m, WeekendOvertimeMultiplier = 2m, HolidayOvertimeMultiplier = 2.5m,
        });
        db.SaveChanges();
    }

    /// <summary>DF-64: seeds an auto-closed (ANOMALY) attendance log with NO overtime record.</summary>
    private Guid SeedAutoClosedLog(Guid employeeId, DateOnly date, int inHour, int outHour)
    {
        using var db = Db();
        var id = BaseEntity.NewUuidV7();
        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = id, TenantId = _tenantId, EmployeeId = employeeId,
            ClockIn = At(date, inHour), ClockOut = At(date, outHour),
            Status = "ANOMALY", Source = "SYSTEM",
        });
        db.SaveChanges();
        return id;
    }

    private void SeedOrg()
    {
        using var db = Db();
        _managerId = Guid.NewGuid();
        _reportId = Guid.NewGuid();
        _strangerId = Guid.NewGuid();

        db.Employees.AddRange(
            Emp(_managerId, _managerUserId, "Mary", "Manager", reportsTo: null),
            Emp(_reportId, Guid.NewGuid(), "Rita", "Report", reportsTo: _managerId),
            Emp(_strangerId, Guid.NewGuid(), "Sam", "Stranger", reportsTo: null));
        db.SaveChanges();
    }

    private Employee Emp(Guid id, Guid userId, string first, string last, Guid? reportsTo) => new()
    {
        Id = id,
        TenantId = _tenantId,
        UserId = userId,
        EmployeeNo = $"EMP-{id.ToString()[..4]}",
        FirstName = first,
        LastName = last,
        Email = $"{first}@test.com".ToLowerInvariant(),
        Gender = Gender.Female,
        DateOfJoining = new DateTime(2020, 1, 1),
        DepartmentId = Guid.NewGuid(),
        JobTitleId = Guid.NewGuid(),
        EmploymentType = EmploymentType.FullTime,
        Status = EmployeeStatus.Active,
        IsActive = true,
        ReportsToEmployeeId = reportsTo,
    };

    private static DateOnly Yesterday => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

    private static DateTime At(DateOnly date, int hour, int minute = 0)
        => date.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc);

    /// <summary>Seeds a PENDING regularization for an employee. Optionally links an existing log.</summary>
    private Guid SeedPending(
        Guid employeeId,
        string type = RegularizationType.MissedBoth,
        DateOnly? date = null,
        DateTime? requestedIn = null,
        DateTime? requestedOut = null,
        Guid? linkedLogId = null)
    {
        var d = date ?? Yesterday;
        using var db = Db();
        var id = BaseEntity.NewUuidV7();
        db.AttendanceRegularizations.Add(new AttendanceRegularization
        {
            Id = id,
            TenantId = _tenantId,
            EmployeeId = employeeId,
            AttendanceLogId = linkedLogId,
            Date = d,
            RegularizationType = type,
            RequestedClockIn = requestedIn ?? (RegularizationType.RequiresClockIn(type) ? At(d, 9) : null),
            RequestedClockOut = requestedOut ?? (RegularizationType.RequiresClockOut(type) ? At(d, 17) : null),
            Reason = "Forgot to punch on this working day",
            Status = RegularizationStatus.Pending,
        });
        db.SaveChanges();
        return id;
    }

    // ── AC-1 / FR-2: approve creates the attendance_log and recalculates ────

    [Fact]
    public async Task Approve_MissedBoth_CreatesLog_AndRecalculates()
    {
        var date = Yesterday;
        var regId = SeedPending(_reportId, RegularizationType.MissedBoth, date, At(date, 9), At(date, 17));

        var result = await Service().ApproveAsync(regId, "Looks correct");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(RegularizationStatus.Approved);
        result.Value.AttendanceLogId.Should().NotBeNull();
        // 9-17 = 8h = 480 gross; auto-break only above 360-min threshold default -> 480-60 = 420.
        result.Value.TotalWorkMinutes.Should().Be(420);
        result.Value.AttendanceStatus.Should().NotBeNull();

        using var db = Db();
        var log = db.AttendanceLogs.Single();
        log.EmployeeId.Should().Be(_reportId);
        log.ClockIn.Should().Be(At(date, 9));
        log.ClockOut.Should().Be(At(date, 17));
        log.TotalWorkMinutes.Should().Be(420);

        // FR-6 / NFR-4: an immutable APPROVED decision-history row exists.
        var history = db.RegularizationApprovalHistories.Single();
        history.RegularizationId.Should().Be(regId);
        history.ApproverEmployeeId.Should().Be(_managerId);
        history.Action.Should().Be(RegularizationApprovalAction.Approved);
        history.Comment.Should().Be("Looks correct");
    }

    // ── AC-1 / FR-2 / BR-5 (linked log): approve updates the existing open log ──

    [Fact]
    public async Task Approve_MissedClockOut_UpdatesLinkedOpenLog()
    {
        var date = Yesterday;
        Guid logId;
        using (var db = Db())
        {
            logId = BaseEntity.NewUuidV7();
            db.AttendanceLogs.Add(new AttendanceLog
            {
                Id = logId, TenantId = _tenantId, EmployeeId = _reportId,
                ClockIn = At(date, 9), ClockOut = null, Source = "WEB",
            });
            db.SaveChanges();
        }

        var regId = SeedPending(
            _reportId, RegularizationType.MissedClockOut, date,
            requestedOut: At(date, 17), linkedLogId: logId);

        var result = await Service().ApproveAsync(regId, null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AttendanceLogId.Should().Be(logId);

        using var verify = Db();
        verify.AttendanceLogs.Should().ContainSingle();   // updated in place, not duplicated.
        var log = verify.AttendanceLogs.Single(a => a.Id == logId);
        log.ClockOut.Should().Be(At(date, 17));           // the existing clock-in + requested clock-out.
        log.TotalWorkMinutes.Should().Be(420);
    }

    // ── NFR-2: atomic — both the regularization status and the log update commit together ──

    [Fact]
    public async Task Approve_PersistsStatusAndLogTogether()
    {
        var regId = SeedPending(_reportId);

        (await Service().ApproveAsync(regId, null)).IsSuccess.Should().BeTrue();

        using var db = Db();
        db.AttendanceRegularizations.Single(r => r.Id == regId).Status
            .Should().Be(RegularizationStatus.Approved);
        db.AttendanceLogs.Should().ContainSingle();
    }

    // ── FR-3 / ISSUE-085: an on-time (within-grace) regularized punch persists late_minutes = 0 ──

    private Guid SeedDefaultShift(int graceMinutes)
    {
        using var db = Db();
        var shiftId = BaseEntity.NewUuidV7();
        db.Shifts.Add(new Shift
        {
            Id = shiftId, TenantId = _tenantId, Name = "General Shift",
            Type = ShiftType.Single, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0),
            GracePeriodMinutes = graceMinutes, WorkingDays = [1, 2, 3, 4, 5],
            IsDefault = true, IsActive = true,
        });
        db.SaveChanges();
        return shiftId;
    }

    [Fact]
    public async Task Approve_OnTimeWithinGrace_PersistsLateMinutesZero()
    {
        SeedDefaultShift(graceMinutes: 20);
        var date = Yesterday;
        // Clock-in 09:15 vs 09:00 start with 20-min grace → within grace → on time.
        var regId = SeedPending(
            _reportId, RegularizationType.MissedBoth, date, At(date, 9, 15), At(date, 17));

        (await Service().ApproveAsync(regId, "Verified")).IsSuccess.Should().BeTrue();

        using var db = Db();
        var log = db.AttendanceLogs.Single();
        log.IsLate.Should().BeFalse();
        log.LateMinutes.Should().Be(0);       // FR-3: 0 when not late (no leftover past-start magnitude).
        log.LateByMinutes.Should().Be(0);
    }

    [Fact]
    public async Task Approve_LatePunch_PersistsRealLateMinutes()
    {
        SeedDefaultShift(graceMinutes: 10);
        var date = Yesterday;
        // Clock-in 09:30 vs 09:00 start with 10-min grace → 30 past start, 20 past grace → late.
        var regId = SeedPending(
            _reportId, RegularizationType.MissedBoth, date, At(date, 9, 30), At(date, 17));

        (await Service().ApproveAsync(regId, "Verified")).IsSuccess.Should().BeTrue();

        using var db = Db();
        var log = db.AttendanceLogs.Single();
        log.IsLate.Should().BeTrue();
        log.LateMinutes.Should().Be(30);      // real minutes-past-start retained on a genuinely late row.
        log.LateByMinutes.Should().Be(20);
    }

    // ── AC-2 / FR-3 / BR-1: reject stores the reason, leaves the log untouched ──

    [Fact]
    public async Task Reject_WithReason_SetsRejected_AndDoesNotTouchLog()
    {
        var regId = SeedPending(_reportId);

        var result = await Service().RejectAsync(regId, "Times do not match the access-card logs");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(RegularizationStatus.Rejected);

        using var db = Db();
        db.AttendanceLogs.Should().BeEmpty();             // FR-3: log not created on reject.
        var history = db.RegularizationApprovalHistories.Single();
        history.Action.Should().Be(RegularizationApprovalAction.Rejected);
        history.Comment.Should().Be("Times do not match the access-card logs");
    }

    [Fact]
    public async Task Reject_WithShortReason_IsRejected()
    {
        var regId = SeedPending(_reportId);

        var result = await Service().RejectAsync(regId, "too short");   // < 10 chars.

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    // ── FR-7 / AC-5: not-a-team-member denial with the EXACT story message ──

    [Fact]
    public async Task Approve_NonTeamEmployee_IsDeniedWithExactMessage()
    {
        var regId = SeedPending(_strangerId);   // stranger does not report to the manager.

        var result = await Service().ApproveAsync(regId, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Be("You are not authorized to approve requests for this employee.");
    }

    // ── BR-6: a manager cannot approve their OWN regularization ──────────────

    [Fact]
    public async Task Approve_OwnRequest_IsDenied()
    {
        var regId = SeedPending(_managerId);    // the manager's own request.

        var result = await Service().ApproveAsync(regId, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("self_approval");
    }

    // ── BR-3: an already-actioned request is immutable ──────────────────────

    [Fact]
    public async Task Approve_AlreadyApproved_IsRejected()
    {
        var regId = SeedPending(_reportId);
        (await Service().ApproveAsync(regId, null)).IsSuccess.Should().BeTrue();

        var second = await Service().ApproveAsync(regId, null);

        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("already_actioned");
    }

    // ── BR-5: payroll lock re-checked at approval time ──────────────────────

    [Fact]
    public async Task Approve_DateInLockedPayrollPeriod_IsBlockedWithExactMessage()
    {
        var date = Yesterday;
        var regId = SeedPending(_reportId, RegularizationType.MissedBoth, date);

        using (var db = Db())
        {
            db.AttendancePeriodLocks.Add(new AttendancePeriodLock
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId,
                PeriodStart = date.AddDays(-2), PeriodEnd = date.AddDays(2),
                IsLocked = true,
            });
            db.SaveChanges();
        }

        var result = await Service().ApproveAsync(regId, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("payroll_period_locked");
        result.Error.Should().Be("This date falls within a locked payroll period. Please contact HR.");
    }

    [Fact]
    public async Task Approve_NotFound_Returns404()
    {
        var result = await Service().ApproveAsync(Guid.NewGuid(), null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── FR-1 / AC-3: pending queue returns direct reports' pending requests ──

    [Fact]
    public async Task GetPending_ReturnsDirectReportsPendingRequests()
    {
        SeedPending(_reportId, date: Yesterday);
        SeedPending(_strangerId, date: Yesterday);   // not a direct report -> excluded.

        var result = await Service().GetPendingForManagerAsync(new PendingRegularizationQueryParams());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle();
        var item = result.Value.Items[0];
        item.EmployeeId.Should().Be(_reportId);
        item.EmployeeName.Should().Be("Rita Report");
        item.Reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetPending_ExcludesAlreadyActionedRequests()
    {
        var regId = SeedPending(_reportId);
        (await Service().RejectAsync(regId, "Rejected for a documented reason")).IsSuccess.Should().BeTrue();

        var result = await Service().GetPendingForManagerAsync(new PendingRegularizationQueryParams());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().BeEmpty();
    }

    // ── BR-7: bulk approve with mixed per-item outcomes ─────────────────────

    [Fact]
    public async Task BulkApprove_ProcessesEachItemIndependently()
    {
        var ok1 = SeedPending(_reportId, date: Yesterday);
        var ok2 = SeedPending(_reportId, date: Yesterday.AddDays(-1));
        var denied = SeedPending(_strangerId, date: Yesterday);   // not a direct report.

        var result = await Service().BulkApproveAsync(new[] { ok1, ok2, denied }, "bulk ok");

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalRequested.Should().Be(3);
        result.Value.SucceededCount.Should().Be(2);
        result.Value.FailedCount.Should().Be(1);

        result.Value.Items.Single(i => i.RegularizationId == denied).Succeeded.Should().BeFalse();
        result.Value.Items.Single(i => i.RegularizationId == ok1).Succeeded.Should().BeTrue();

        using var db = Db();
        db.AttendanceRegularizations
            .Count(r => r.Status == RegularizationStatus.Approved).Should().Be(2);
    }

    [Fact]
    public async Task BulkApprove_EmptyList_IsRejected()
    {
        var result = await Service().BulkApproveAsync(Array.Empty<Guid>(), null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    // ── No tenant context ───────────────────────────────────────────────────

    [Fact]
    public async Task Approve_NoTenantContext_IsRejected()
    {
        var regId = SeedPending(_reportId);

        var ctx = Substitute.For<ITenantContext>();
        ctx.IsResolved.Returns(false);
        var noCtxDb = TestDbContextFactory.Create(ctx, _dbName);
        var svc = new RegularizationApprovalService(
            noCtxDb, ctx, _currentUser,
            new ShiftService(noCtxDb, ctx, _currentUser, Substitute.For<ILogger<ShiftService>>()),
            _logger);

        var result = await svc.ApproveAsync(regId, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    // ── DF-64: a regularized OT day must produce a payable OvertimeRecord ──

    /// <summary>DF-64: seeds an existing auto-detected OvertimeRecord for a log (a prior clock-out's record).</summary>
    private Guid SeedAutoDetectedOtRecord(
        Guid logId, Guid employeeId, DateOnly date, int minutes,
        string status = OvertimeStatus.Pending, bool payrollReady = false)
    {
        using var db = Db();
        var id = BaseEntity.NewUuidV7();
        db.OvertimeRecords.Add(new OvertimeRecord
        {
            Id = id, TenantId = _tenantId, EmployeeId = employeeId,
            AttendanceLogId = logId, Date = date, OvertimeMinutes = minutes,
            ApprovedMinutes = status == OvertimeStatus.Approved ? minutes : null, Multiplier = 1.5m,
            Type = OvertimeType.AutoDetected, Status = status, IsPayrollReady = payrollReady,
            Reason = "Auto-detected overtime on clock-out.", CalculationBasis = "seed",
        });
        db.SaveChanges();
        return id;
    }

    [Fact]
    [Trait("TC", "TC-ATT-006-64")]
    public async Task Approve_RegularizedOtDay_CreatesPayableOvertimeRecord_DF64()
    {
        var date = Yesterday;
        SeedOvertimeSettings();
        // An auto-closed ANOMALY day (8:00 -> system 23:00) with NO overtime record — the DF-64 gap.
        var logId = SeedAutoClosedLog(_reportId, date, inHour: 8, outHour: 23);
        // The employee regularizes the clock-out to 20:00 -> 8-20 = 720 gross, 60 break -> 660 net -> OT 180.
        var regId = SeedPending(_reportId, RegularizationType.MissedClockOut, date,
            requestedOut: At(date, 20), linkedLogId: logId);

        using (var db = Db())
            (await ServiceWithOvertime(db).ApproveAsync(regId, "Approved OT")).IsSuccess.Should().BeTrue();

        using var verify = Db();
        var ot = verify.OvertimeRecords.Should().ContainSingle().Subject;
        ot.AttendanceLogId.Should().Be(logId);
        ot.OvertimeMinutes.Should().Be(180, "660 net - 480 standard = 180 OT minutes");
        ot.Type.Should().Be(OvertimeType.AutoDetected);
        ot.Status.Should().Be(OvertimeStatus.Pending, "the record enters the NORMAL OT approval flow, not auto-approved");
        ot.IsPayrollReady.Should().BeFalse("Pending OT is not payroll-ready until separately approved");

        verify.AttendanceLogs.Single(l => l.Id == logId).OvertimeMinutes
            .Should().Be(180, "the log and the payable record agree");
    }

    [Fact]
    [Trait("TC", "TC-ATT-006-64")]
    public async Task Approve_RegularizationEliminatesOvertime_RemovesStaleAutoDetectedRecord_DF64()
    {
        var date = Yesterday;
        SeedOvertimeSettings();
        // A day that previously had OT (8-20) with an auto-detected record from a prior clock-out...
        var logId = SeedAutoClosedLog(_reportId, date, inHour: 8, outHour: 20);
        SeedAutoDetectedOtRecord(logId, _reportId, date, minutes: 180);
        // ...is regularized DOWN to a short day (8-15 = 420 gross, 60 break -> 360 net < 480 standard -> no OT).
        var regId = SeedPending(_reportId, RegularizationType.MissedClockOut, date,
            requestedOut: At(date, 15), linkedLogId: logId);

        using (var db = Db())
            (await ServiceWithOvertime(db).ApproveAsync(regId, "Corrected down")).IsSuccess.Should().BeTrue();

        using var verify = Db();
        verify.OvertimeRecords.Where(o => o.AttendanceLogId == logId && o.Type == OvertimeType.AutoDetected)
            .Should().BeEmpty("the correction eliminated the overtime, so the stale auto-detected record is removed");
    }

    [Fact]
    [Trait("TC", "TC-ATT-006-64")]
    public async Task Approve_RegularizationChangesOvertime_ReplacesRecord_NoDuplicate_DF64()
    {
        var date = Yesterday;
        SeedOvertimeSettings();
        // A day with an EXISTING auto-detected OT record of 120 (a prior clock-out's value)...
        var logId = SeedAutoClosedLog(_reportId, date, inHour: 8, outHour: 23);
        SeedAutoDetectedOtRecord(logId, _reportId, date, minutes: 120);
        // ...is regularized to 8-20 -> 660 net -> OT 180. The stale 120 must be REPLACED, not duplicated.
        var regId = SeedPending(_reportId, RegularizationType.MissedClockOut, date,
            requestedOut: At(date, 20), linkedLogId: logId);

        using (var db = Db())
            (await ServiceWithOvertime(db).ApproveAsync(regId, "Corrected up")).IsSuccess.Should().BeTrue();

        using var verify = Db();
        var ot = verify.OvertimeRecords
            .Where(o => o.AttendanceLogId == logId && o.Type == OvertimeType.AutoDetected)
            .Should().ContainSingle("the prior auto-detected record is replaced, not duplicated").Subject;
        ot.OvertimeMinutes.Should().Be(180, "the record carries the RECOMPUTED overtime, not the stale 120");
        ot.Status.Should().Be(OvertimeStatus.Pending, "a changed OT record re-enters the approval flow");
    }

    [Fact]
    [Trait("TC", "TC-ATT-006-64")]
    public async Task Approve_RegularizationOnDayWithApprovedOt_PreservesApprovedRecord_DF64()
    {
        var date = Yesterday;
        SeedOvertimeSettings();
        var logId = SeedAutoClosedLog(_reportId, date, inHour: 8, outHour: 20);
        // A manager already APPROVED this day's OT (payroll-ready). Deleting it would drop paid OT AND
        // cascade-delete its immutable OvertimeApprovalHistory — the guard must preserve it.
        var approvedId = SeedAutoDetectedOtRecord(logId, _reportId, date, minutes: 120,
            status: OvertimeStatus.Approved, payrollReady: true);
        // A LATER same-day regularization is approved (to 22:00 -> would recompute to more OT).
        var regId = SeedPending(_reportId, RegularizationType.MissedClockOut, date,
            requestedOut: At(date, 22), linkedLogId: logId);

        using (var db = Db())
            (await ServiceWithOvertime(db).ApproveAsync(regId, "Late correction")).IsSuccess.Should().BeTrue();

        using var verify = Db();
        var records = verify.OvertimeRecords.Where(o => o.AttendanceLogId == logId).ToList();
        records.Should().ContainSingle("the approved record is preserved, not replaced by a new pending one");
        var ot = records.Single();
        ot.Id.Should().Be(approvedId, "the SAME approved record survives the later time correction");
        ot.Status.Should().Be(OvertimeStatus.Approved, "an approved OT decision is not silently re-opened");
        ot.OvertimeMinutes.Should().Be(120, "the approved value is untouched");
        ot.IsPayrollReady.Should().BeTrue("the payroll-ready approved OT stands");
    }
}
