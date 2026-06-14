using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Monthly attendance summary aggregation + read service (US-ATT-007). Rolls up the data the prior
/// Attendance stories produce: AttendanceLog (clock-in/out + total_work_minutes), OvertimeRecord
/// (APPROVED overtime only), AttendanceRegularization (approved → treated as normal, BR-7), the
/// resolved Shift/EmployeeShift (working-days / start+grace / minimum, US-ATT-005), the tenant Holiday
/// calendar (US-LV-007), and approved LeaveRequest days (BR-6 reconciliation).
///
/// MATERIALIZED CACHE (FR-8): the story asks for a Redis cache. This codebase has no Redis — the
/// <c>attendance_monthly_summary</c> table IS the cache: <see cref="GenerateAsync"/> upserts computed
/// rows and <see cref="GetMonthlyAsync"/> serves from them (computing on-demand if absent, AC-3).
///
/// LATE / EARLY (US-ATT-008 not built): FR-3/AC-1 need late + early-departure counts. They are computed
/// HERE from the resolved shift: late = clock-in after shift start_time + grace_period; early departure
/// = clock-out before shift end_time. If a shift can't be resolved, counts are 0. US-ATT-008 may later
/// own/refine this — the computation is self-contained.
///
/// TENANT ISOLATION (NFR-3): every query runs under the EF global query filter (TenantId == tenant).
/// No PostgreSQL RLS (same deferral as the rest of the module). The Hangfire jobs set the tenant
/// context per tenant before calling in (mirrors AutoClockOutJob, US-ATT-002).
/// </summary>
public sealed class AttendanceSummaryService : IAttendanceSummaryService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IReportExportStorage _exportStorage;
    private readonly IBackgroundJobClient? _backgroundJobs;
    private readonly ILogger<AttendanceSummaryService> _logger;

    /// <summary>FR-7: exports at or below this employee count render synchronously; larger go to Hangfire.</summary>
    public const int SyncExportEmployeeThreshold = 1000;

    public AttendanceSummaryService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IReportExportStorage exportStorage,
        ILogger<AttendanceSummaryService> logger,
        IBackgroundJobClient? backgroundJobs = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _exportStorage = exportStorage;
        _logger = logger;
        _backgroundJobs = backgroundJobs;
    }

    // ══════════════════════════════════════════════════════════════
    //  Public API
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<MonthlySummaryResult>> GetMonthlyAsync(
        int year, int month, MonthlySummaryFilter filter, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<MonthlySummaryResult>.Failure("Tenant context is not resolved.", 400);
        if (month is < 1 or > 12)
            return Result<MonthlySummaryResult>.Failure("Month must be between 1 and 12.", 400, "invalid_month");

        var yearMonth = YearMonth(year, month);

        // Candidate employees for the filter (also drives which materialized rows we surface).
        var employees = await FilteredEmployeesAsync(filter, cancellationToken);
        var employeeIds = employees.Select(e => e.Id).ToList();

        // Read materialized rows for the month. If none exist for these employees yet, compute on-demand
        // (AC-3) for the current/incomplete month or any month not yet generated.
        var existing = await _dbContext.AttendanceMonthlySummaries.AsNoTracking()
            .Where(s => s.YearMonth == yearMonth && employeeIds.Contains(s.EmployeeId))
            .ToListAsync(cancellationToken);

        if (existing.Count == 0 && employeeIds.Count > 0)
        {
            var gen = await GenerateAsync(year, month, cancellationToken);
            if (gen.IsFailure)
                return Result<MonthlySummaryResult>.Failure(gen.Error!, gen.StatusCode ?? 400);

            existing = await _dbContext.AttendanceMonthlySummaries.AsNoTracking()
                .Where(s => s.YearMonth == yearMonth && employeeIds.Contains(s.EmployeeId))
                .ToListAsync(cancellationToken);
        }

        var deptNames = await DepartmentNamesAsync(employees, cancellationToken);
        var empById = employees.ToDictionary(e => e.Id);
        var summaryByEmp = existing.ToDictionary(s => s.EmployeeId);

        var rows = new List<EmployeeMonthlySummaryDto>();
        foreach (var emp in employees)
        {
            if (!summaryByEmp.TryGetValue(emp.Id, out var s))
                continue;   // no materialized row for this employee/month

            rows.Add(new EmployeeMonthlySummaryDto
            {
                EmployeeId = emp.Id,
                EmployeeName = FullName(emp),
                EmployeeNumber = emp.EmployeeNo,
                DepartmentName = deptNames.GetValueOrDefault(emp.DepartmentId),
                PresentDays = s.TotalPresentDays,
                AbsentDays = s.TotalAbsentDays,
                LateCount = s.TotalLateCount,
                EarlyDepartureCount = s.TotalEarlyDepartureCount,
                WorkMinutes = s.TotalWorkMinutes,
                OvertimeMinutes = s.TotalOvertimeMinutes,
                LeaveDays = s.TotalLeaveDays,
                Holidays = s.TotalHolidays,
                WeeklyOffs = s.TotalWeeklyOffs,
                LopDays = s.LopDays,
                GeneratedAt = s.GeneratedAt,
            });
        }

        rows = rows.OrderBy(r => r.EmployeeNumber, StringComparer.OrdinalIgnoreCase).ToList();

        var banner = BuildBanner(rows);
        DateTime? generatedAt = rows.Count > 0 ? rows.Max(r => r.GeneratedAt) : null;

        return Result<MonthlySummaryResult>.Success(new MonthlySummaryResult
        {
            YearMonth = yearMonth,
            Rows = rows,
            Banner = banner,
            GeneratedAt = generatedAt,
        });
    }

    public async Task<Result<EmployeeDailyBreakdownResult>> GetEmployeeBreakdownAsync(
        Guid employeeId, int year, int month, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<EmployeeDailyBreakdownResult>.Failure("Tenant context is not resolved.", 400);
        if (month is < 1 or > 12)
            return Result<EmployeeDailyBreakdownResult>.Failure("Month must be between 1 and 12.", 400, "invalid_month");

        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (employee is null)
            return Result<EmployeeDailyBreakdownResult>.Failure("Employee not found.", 404);

        var (monthStart, monthEnd, lastDay) = MonthBounds(year, month);
        var ctx = await LoadEmployeeMonthContextAsync(employee, monthStart, monthEnd, cancellationToken);

        var days = new List<DailyBreakdownDto>();
        for (var date = monthStart; date <= lastDay; date = date.AddDays(1))
        {
            var day = ComputeDay(date, ctx);
            days.Add(new DailyBreakdownDto
            {
                Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Status = day.Status,
                ClockIn = day.ClockIn,
                ClockOut = day.ClockOut,
                WorkMinutes = day.WorkMinutes,
                IsRegularized = day.IsRegularized,
                IsLate = day.IsLate,
                IsEarlyDeparture = day.IsEarlyDeparture,
            });
        }

        return Result<EmployeeDailyBreakdownResult>.Success(new EmployeeDailyBreakdownResult
        {
            EmployeeId = employee.Id,
            EmployeeName = FullName(employee),
            YearMonth = YearMonth(year, month),
            Days = days,
        });
    }

    public async Task<Result<SummaryGenerationStatusDto>> GenerateAsync(
        int year, int month, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<SummaryGenerationStatusDto>.Failure("Tenant context is not resolved.", 400);
        if (month is < 1 or > 12)
            return Result<SummaryGenerationStatusDto>.Failure("Month must be between 1 and 12.", 400, "invalid_month");

        var yearMonth = YearMonth(year, month);
        var (monthStart, monthEnd, lastDay) = MonthBounds(year, month);

        // Compute for ALL non-terminated employees (the materialized table is filter-agnostic; the read
        // path applies department/location/shift/status filters on top).
        var employees = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.Status != EmployeeStatus.Terminated)
            .ToListAsync(cancellationToken);

        // Pre-load all existing summary rows for the month for these employees (upsert).
        var employeeIds = employees.Select(e => e.Id).ToList();
        var existing = await _dbContext.AttendanceMonthlySummaries
            .Where(s => s.YearMonth == yearMonth && employeeIds.Contains(s.EmployeeId))
            .ToListAsync(cancellationToken);
        var existingByEmp = existing.ToDictionary(s => s.EmployeeId);

        var now = DateTime.UtcNow;
        foreach (var employee in employees)
        {
            var computed = await ComputeMonthAsync(employee, monthStart, monthEnd, lastDay, cancellationToken);

            if (existingByEmp.TryGetValue(employee.Id, out var row))
            {
                Apply(row, computed, now);
            }
            else
            {
                var fresh = new AttendanceMonthlySummary
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantContext.TenantId,
                    EmployeeId = employee.Id,
                    YearMonth = yearMonth,
                };
                Apply(fresh, computed, now);
                _dbContext.AttendanceMonthlySummaries.Add(fresh);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Attendance monthly summary generated for {YearMonth}: {Count} employees (tenant {TenantId}).",
            yearMonth, employees.Count, _tenantContext.TenantId);

        return Result<SummaryGenerationStatusDto>.Success(new SummaryGenerationStatusDto
        {
            YearMonth = yearMonth,
            Status = "COMPLETED",
            GeneratedAt = now,
        });
    }

    public async Task<Result<MonthlySummaryExportResult>> ExportAsync(
        int year, int month, string format, MonthlySummaryFilter filter,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<MonthlySummaryExportResult>.Failure("Tenant context is not resolved.", 400);

        var normalizedFormat = NormalizeFormat(format);
        if (normalizedFormat is null)
            return Result<MonthlySummaryExportResult>.Failure(
                "Export format must be one of csv, xlsx, pdf.", 400, "invalid_format");

        var summary = await GetMonthlyAsync(year, month, filter, cancellationToken);
        if (summary.IsFailure)
            return Result<MonthlySummaryExportResult>.Failure(summary.Error!, summary.StatusCode ?? 400);

        var result = summary.Value!;
        int rowCount = result.Rows.Count;

        // FR-7: large exports (> 1,000 employees) route to a Hangfire job (notification DEFERRED, US-NTF).
        if (rowCount > SyncExportEmployeeThreshold)
        {
            if (_backgroundJobs is null)
            {
                // No Hangfire client (e.g. unit context): report that background routing is required
                // rather than rendering a huge file inline.
                return Result<MonthlySummaryExportResult>.Success(new MonthlySummaryExportResult
                {
                    Queued = true,
                    JobId = null,
                    RowCount = rowCount,
                });
            }

            var reportId = BaseEntity.NewUuidV7();
            var tenantId = _tenantContext.TenantId;

            var jobId = _backgroundJobs.Enqueue<IAttendanceSummaryExportJob>(j => j.RunAsync(
                tenantId, reportId, year, month, normalizedFormat, filter, CancellationToken.None));

            _logger.LogInformation(
                "Attendance summary export {ReportId} ({Rows} employees > {Threshold}) queued as Hangfire " +
                "job {JobId} for tenant {TenantId}. Notification deferred (US-NTF).",
                reportId, rowCount, SyncExportEmployeeThreshold, jobId, tenantId);

            return Result<MonthlySummaryExportResult>.Success(new MonthlySummaryExportResult
            {
                Queued = true,
                JobId = jobId,
                RowCount = rowCount,
            });
        }

        var (content, fileName, contentType) = RenderExport(year, month, normalizedFormat, result);
        return Result<MonthlySummaryExportResult>.Success(new MonthlySummaryExportResult
        {
            Queued = false,
            FileContent = content,
            FileName = fileName,
            ContentType = contentType,
            RowCount = rowCount,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Aggregation core (BR-1..BR-7, NFR-5)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Per-employee per-month context: their attendance logs, approved leave days, holidays for their
    /// location, the resolved shift, and the tenant half-day flag — loaded once so the per-day loop is
    /// pure in-memory (NFR-5 minute-accurate).
    /// </summary>
    private sealed class MonthContext
    {
        public required Dictionary<DateOnly, AttendanceLog> LogsByDate { get; init; }
        public required HashSet<DateOnly> RegularizedDates { get; init; }
        public required HashSet<DateOnly> ApprovedLeaveFullDates { get; init; }
        public required HashSet<DateOnly> ApprovedLeaveHalfDates { get; init; }
        public required HashSet<DateOnly> HolidayDates { get; init; }
        public required HashSet<int> WorkingDays { get; init; }        // 1=Mon..7=Sun
        public required int StandardMinutes { get; init; }
        public required int MinimumMinutes { get; init; }
        public required int GracePeriodMinutes { get; init; }
        public required TimeOnly? ShiftStart { get; init; }
        public required TimeOnly? ShiftEnd { get; init; }
        public required bool HalfDayEnabled { get; init; }
        public required HashSet<DateOnly> ApprovedOvertimeDates { get; init; }
        public required Dictionary<DateOnly, int> ApprovedOvertimeByDate { get; init; }
    }

    private async Task<MonthContext> LoadEmployeeMonthContextAsync(
        Employee employee, DateOnly monthStart, DateOnly monthEnd, CancellationToken ct)
    {
        // Attendance logs for the month (match on the UTC calendar day of clock-in).
        var logs = await _dbContext.AttendanceLogs.AsNoTracking()
            .Where(a => a.EmployeeId == employee.Id
                && a.ClockIn >= monthStart.ToDateTime(TimeOnly.MinValue)
                && a.ClockIn < monthEnd.AddDays(1).ToDateTime(TimeOnly.MinValue))
            .ToListAsync(ct);

        // One log per date; if multiple, keep the one with the most worked minutes (best signal).
        var logsByDate = logs
            .GroupBy(a => DateOnly.FromDateTime(a.ClockIn))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(a => a.TotalWorkMinutes ?? 0).First());

        // Regularized dates (approved regularization → treated as normal, BR-7). Approved regularizations
        // mutate the attendance_log directly (US-ATT-004), so the dates are also surfaced as a flag.
        var regularizedDates = (await _dbContext.AttendanceRegularizations.AsNoTracking()
                .Where(r => r.EmployeeId == employee.Id
                    && r.Status == RegularizationStatus.Approved
                    && r.Date >= monthStart && r.Date <= monthEnd)
                .Select(r => r.Date)
                .ToListAsync(ct))
            .ToHashSet();

        // Approved leave in the month (BR-6). Expand each request's [start,end] into the month's dates.
        var leaveRequests = await _dbContext.LeaveRequests.AsNoTracking()
            .Where(lr => lr.EmployeeId == employee.Id
                && lr.Status == LeaveRequestStatus.Approved
                && lr.StartDate <= monthEnd && lr.EndDate >= monthStart)
            .Select(lr => new { lr.StartDate, lr.EndDate, lr.IsHalfDay })
            .ToListAsync(ct);

        var fullLeave = new HashSet<DateOnly>();
        var halfLeave = new HashSet<DateOnly>();
        foreach (var lr in leaveRequests)
        {
            var from = lr.StartDate < monthStart ? monthStart : lr.StartDate;
            var to = lr.EndDate > monthEnd ? monthEnd : lr.EndDate;
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                if (lr.IsHalfDay && lr.StartDate == lr.EndDate)
                    halfLeave.Add(d);
                else
                    fullLeave.Add(d);
            }
        }

        // Holidays for the employee's location (LocationId match OR tenant-wide null). Public only (BR-4).
        var holidayDates = (await _dbContext.Holidays.AsNoTracking()
                .Where(h => h.IsActive && h.Type == HolidayType.Public
                    && h.Date >= monthStart && h.Date <= monthEnd
                    && (h.LocationId == null || h.LocationId == employee.LocationId))
                .Select(h => h.Date)
                .ToListAsync(ct))
            .ToHashSet();

        // Resolved shift for the month (use the assignment effective at month-start, falling back to the
        // tenant default; minute math is from start/end span). Single resolution per month is sufficient
        // for the working-days / start+grace / minimum signals (rotation per-day is out of scope here).
        var (workingDays, standardMinutes, minimumMinutes, grace, shiftStart, shiftEnd) =
            await ResolveShiftSignalsAsync(employee.Id, monthStart, ct);

        var settings = await _dbContext.AttendanceSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        bool halfDayEnabled = settings?.HalfDayEnabled ?? false;
        // Minimum threshold falls back to tenant setting when the shift carries none.
        if (minimumMinutes <= 0)
            minimumMinutes = settings?.MinimumWorkMinutes ?? 240;
        if (standardMinutes <= 0)
            standardMinutes = settings?.StandardWorkMinutes ?? 480;

        // Approved overtime in the month (BR — approved only).
        var overtimeRows = await _dbContext.OvertimeRecords.AsNoTracking()
            .Where(o => o.EmployeeId == employee.Id
                && o.Status == OvertimeStatus.Approved
                && o.Date >= monthStart && o.Date <= monthEnd)
            .Select(o => new { o.Date, Minutes = o.ApprovedMinutes ?? o.OvertimeMinutes })
            .ToListAsync(ct);
        var otByDate = overtimeRows
            .GroupBy(o => o.Date)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Minutes));

        return new MonthContext
        {
            LogsByDate = logsByDate,
            RegularizedDates = regularizedDates,
            ApprovedLeaveFullDates = fullLeave,
            ApprovedLeaveHalfDates = halfLeave,
            HolidayDates = holidayDates,
            WorkingDays = workingDays,
            StandardMinutes = standardMinutes,
            MinimumMinutes = minimumMinutes,
            GracePeriodMinutes = grace,
            ShiftStart = shiftStart,
            ShiftEnd = shiftEnd,
            HalfDayEnabled = halfDayEnabled,
            ApprovedOvertimeDates = otByDate.Keys.ToHashSet(),
            ApprovedOvertimeByDate = otByDate,
        };
    }

    /// <summary>The computed per-month aggregate for one employee.</summary>
    private readonly record struct MonthAggregate(
        decimal PresentDays, decimal AbsentDays, int LateCount, int EarlyDepartureCount,
        int WorkMinutes, int OvertimeMinutes, decimal LeaveDays, int Holidays, int WeeklyOffs, decimal LopDays);

    private async Task<MonthAggregate> ComputeMonthAsync(
        Employee employee, DateOnly monthStart, DateOnly monthEnd, DateOnly lastIncludedDay, CancellationToken ct)
    {
        var ctx = await LoadEmployeeMonthContextAsync(employee, monthStart, monthEnd, ct);

        decimal present = 0m, absent = 0m, leave = 0m, lop = 0m;
        int late = 0, early = 0, workMinutes = 0, holidays = 0, weeklyOffs = 0, overtime = 0;

        for (var date = monthStart; date <= lastIncludedDay; date = date.AddDays(1))
        {
            var day = ComputeDay(date, ctx);

            switch (day.Status)
            {
                case "HOLIDAY":
                    holidays++;
                    break;
                case "WEEKLY_OFF":
                    weeklyOffs++;
                    break;
                case "LEAVE":
                    leave += 1m;
                    break;
                case "HALF_DAY" when day.IsLeaveHalf:
                    // Half-day leave: 0.5 leave + 0.5 present when worked, else 0.5 leave + 0.5 absent.
                    leave += 0.5m;
                    if (day.WorkMinutes is > 0) present += 0.5m; else { absent += 0.5m; lop += 0.5m; }
                    break;
                case "HALF_DAY":
                    present += 0.5m;
                    break;
                case "PRESENT":
                    present += 1m;
                    break;
                case "ABSENT":
                    absent += 1m;
                    lop += 1m;   // BR-3: absent not covered by leave is LOP.
                    break;
            }

            if (day.WorkMinutes is { } wm) workMinutes += wm;
            if (day.IsLate) late++;
            if (day.IsEarlyDeparture) early++;
            overtime += ctx.ApprovedOvertimeByDate.GetValueOrDefault(date, 0);
        }

        return new MonthAggregate(
            present, absent, late, early, workMinutes, overtime, leave, holidays, weeklyOffs, lop);
    }

    private readonly record struct DayResult(
        string Status, DateTime? ClockIn, DateTime? ClockOut, int? WorkMinutes,
        bool IsRegularized, bool IsLate, bool IsEarlyDeparture, bool IsLeaveHalf);

    /// <summary>
    /// Classifies a single day per BR-1..BR-7. Precedence: HOLIDAY (BR-4) → full LEAVE (BR-6) →
    /// WEEKLY_OFF (BR-4) → worked (PRESENT / HALF_DAY by BR-1/BR-5) → half-day-leave → ABSENT (BR-2).
    /// </summary>
    private static DayResult ComputeDay(DateOnly date, MonthContext ctx)
    {
        bool isHoliday = ctx.HolidayDates.Contains(date);
        bool isWorkingDay = ctx.WorkingDays.Count == 0 || ctx.WorkingDays.Contains(IsoDay(date));
        bool hasLog = ctx.LogsByDate.TryGetValue(date, out var log);
        bool fullLeave = ctx.ApprovedLeaveFullDates.Contains(date);
        bool halfLeave = ctx.ApprovedLeaveHalfDates.Contains(date);
        bool regularized = ctx.RegularizedDates.Contains(date);

        DateTime? clockIn = hasLog ? log!.ClockIn : null;
        DateTime? clockOut = hasLog ? log!.ClockOut : null;
        int? workMinutes = hasLog ? log!.TotalWorkMinutes : null;

        bool isLate = false, isEarly = false;
        if (hasLog)
        {
            (isLate, isEarly) = EvaluateLateEarly(log!, ctx);
        }

        // BR-4: public holidays are excluded from present/absent.
        if (isHoliday)
            return new DayResult("HOLIDAY", clockIn, clockOut, workMinutes, regularized, isLate, isEarly, false);

        // BR-6: full approved leave is never absent.
        if (fullLeave)
            return new DayResult("LEAVE", clockIn, clockOut, workMinutes, regularized, isLate, isEarly, false);

        // BR-4: weekly off (not a scheduled working day) is excluded — but a worked weekly-off still
        // counts work minutes (only when there is a log).
        if (!isWorkingDay && !hasLog)
            return new DayResult("WEEKLY_OFF", null, null, null, false, false, false, false);

        if (hasLog && workMinutes is { } wm)
        {
            // BR-5: half-day = total hours less than the shift standard but at/above 50% of standard,
            // when the tenant enables half-day. Otherwise (or below 50%) a worked day with any record is
            // counted as a full present day — LOP/absence is reserved for NO record on a working day
            // (BR-2), so a short-but-real attendance day is not penalized here.
            if (ctx.HalfDayEnabled && wm < ctx.StandardMinutes && wm >= ctx.StandardMinutes / 2)
                return new DayResult("HALF_DAY", clockIn, clockOut, workMinutes, regularized, isLate, isEarly, false);

            return new DayResult("PRESENT", clockIn, clockOut, workMinutes, regularized, isLate, isEarly, false);
        }

        // BR-6 half-day leave with no work record: half leave, the other half is absence.
        if (halfLeave)
            return new DayResult("HALF_DAY", clockIn, clockOut, workMinutes, regularized, isLate, isEarly, true);

        if (!isWorkingDay)
            return new DayResult("WEEKLY_OFF", null, null, null, false, false, false, false);

        // BR-2: scheduled working day, no record, no leave → ABSENT (→ LOP, BR-3).
        return new DayResult("ABSENT", null, null, null, false, false, false, false);
    }

    /// <summary>
    /// US-ATT-008-pending late / early-departure detection from the resolved shift. Late = clock-in
    /// (wall-clock, treated as UTC — tenant-timezone deferred) after shift start + grace. Early
    /// departure = clock-out before shift end. Returns (false,false) when the shift has no start/end.
    /// </summary>
    private static (bool IsLate, bool IsEarly) EvaluateLateEarly(AttendanceLog log, MonthContext ctx)
    {
        bool late = false, early = false;

        if (ctx.ShiftStart is { } start)
        {
            var threshold = start.Add(TimeSpan.FromMinutes(ctx.GracePeriodMinutes));
            var clockInTime = TimeOnly.FromDateTime(log.ClockIn);
            // Only compare same-day wall-clock; ignore overnight wrap (night-shift late detection deferred).
            late = clockInTime > threshold;
        }

        if (ctx.ShiftEnd is { } end && log.ClockOut is { } co)
        {
            var clockOutTime = TimeOnly.FromDateTime(co);
            early = clockOutTime < end;
        }

        return (late, early);
    }

    /// <summary>
    /// Resolves the working-days set, standard/minimum minutes, grace, and start/end times for an
    /// employee at a date. Uses the assignment effective on the date, else the tenant default shift.
    /// Returns empty working-days (= treat every day as working) when no shift resolves.
    /// </summary>
    private async Task<(HashSet<int> WorkingDays, int Standard, int Minimum, int Grace, TimeOnly? Start, TimeOnly? End)>
        ResolveShiftSignalsAsync(Guid employeeId, DateOnly date, CancellationToken ct)
    {
        var assignment = await _dbContext.EmployeeShifts.AsNoTracking()
            .Where(es => es.EmployeeId == employeeId
                && es.EffectiveFrom <= date
                && (es.EffectiveTo == null || es.EffectiveTo >= date))
            .OrderByDescending(es => es.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        Shift? shift = null;
        if (assignment is not null)
        {
            shift = await _dbContext.Shifts.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == assignment.ShiftId, ct);
        }
        shift ??= await _dbContext.Shifts.AsNoTracking()
            .FirstOrDefaultAsync(s => s.IsDefault, ct);

        if (shift is null)
            return (new HashSet<int>(), 0, 0, 0, null, null);

        var workingDays = shift.WorkingDays.ToHashSet();

        int standard = 0;
        if (shift.StartTime is { } st && shift.EndTime is { } en)
        {
            var span = en.ToTimeSpan().TotalMinutes - st.ToTimeSpan().TotalMinutes;
            if (span <= 0) span += 24 * 60;   // overnight shift (§10).
            standard = Math.Max(0, (int)Math.Round(span) - shift.BreakDurationMinutes);
        }
        else if (shift.MinimumHours is { } mh)
        {
            standard = (int)Math.Round(mh * 60m);
        }

        int minimum = shift.MinimumHours is { } min ? (int)Math.Round(min * 60m) : 0;

        return (workingDays, standard, minimum, shift.GracePeriodMinutes, shift.StartTime, shift.EndTime);
    }

    // ══════════════════════════════════════════════════════════════
    //  Filtering / helpers
    // ══════════════════════════════════════════════════════════════

    private async Task<List<Employee>> FilteredEmployeesAsync(
        MonthlySummaryFilter filter, CancellationToken ct)
    {
        IQueryable<Employee> q = _dbContext.Employees.AsNoTracking();

        if (TryParseStatus(filter.Status, out var status))
            q = q.Where(e => e.Status == status);
        else
            q = q.Where(e => e.Status != EmployeeStatus.Terminated);   // default: exclude terminated.

        if (filter.DepartmentId is { } dept)
            q = q.Where(e => e.DepartmentId == dept);
        if (filter.LocationId is { } loc)
            q = q.Where(e => e.LocationId == loc);

        var employees = await q.OrderBy(e => e.EmployeeNo).ToListAsync(ct);

        // Shift filter: keep employees whose effective (or default) shift is the requested one.
        if (filter.ShiftId is { } shiftId)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var assignedToShift = (await _dbContext.EmployeeShifts.AsNoTracking()
                    .Where(es => es.ShiftId == shiftId
                        && es.EffectiveFrom <= today
                        && (es.EffectiveTo == null || es.EffectiveTo >= today))
                    .Select(es => es.EmployeeId)
                    .ToListAsync(ct))
                .ToHashSet();
            employees = employees.Where(e => assignedToShift.Contains(e.Id)).ToList();
        }

        return employees;
    }

    private async Task<Dictionary<Guid, string>> DepartmentNamesAsync(
        IReadOnlyList<Employee> employees, CancellationToken ct)
    {
        var deptIds = employees.Select(e => e.DepartmentId).Distinct().ToList();
        if (deptIds.Count == 0) return new Dictionary<Guid, string>();
        return (await _dbContext.Departments.AsNoTracking()
                .Where(d => deptIds.Contains(d.Id))
                .Select(d => new { d.Id, d.Name })
                .ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.Name);
    }

    private static MonthlySummaryBannerDto BuildBanner(IReadOnlyList<EmployeeMonthlySummaryDto> rows)
    {
        if (rows.Count == 0)
            return new MonthlySummaryBannerDto { TotalEmployees = 0, AverageAttendancePercent = 0m, TotalLopDays = 0m };

        // Attendance % per employee = present / (present + absent) * 100 (scheduled-day denominator);
        // averaged across employees. Guards a zero denominator.
        decimal sumPct = 0m;
        int counted = 0;
        foreach (var r in rows)
        {
            decimal denom = r.PresentDays + r.AbsentDays;
            if (denom <= 0m) continue;
            sumPct += r.PresentDays / denom * 100m;
            counted++;
        }

        return new MonthlySummaryBannerDto
        {
            TotalEmployees = rows.Count,
            AverageAttendancePercent = counted > 0 ? Math.Round(sumPct / counted, 2) : 0m,
            TotalLopDays = rows.Sum(r => r.LopDays),
        };
    }

    private static void Apply(AttendanceMonthlySummary row, MonthAggregate a, DateTime now)
    {
        row.TotalPresentDays = a.PresentDays;
        row.TotalAbsentDays = a.AbsentDays;
        row.TotalLateCount = a.LateCount;
        row.TotalEarlyDepartureCount = a.EarlyDepartureCount;
        row.TotalWorkMinutes = a.WorkMinutes;
        row.TotalOvertimeMinutes = a.OvertimeMinutes;
        row.TotalLeaveDays = a.LeaveDays;
        row.TotalHolidays = a.Holidays;
        row.TotalWeeklyOffs = a.WeeklyOffs;
        row.LopDays = a.LopDays;
        row.GeneratedAt = now;
    }

    private static string FullName(Employee e) => $"{e.FirstName} {e.LastName}".Trim();

    private static string YearMonth(int year, int month) => $"{year:D4}-{month:D2}";

    private static (DateOnly Start, DateOnly End, DateOnly LastIncluded) MonthBounds(int year, int month)
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        // AC-3 / §10: for the current (incomplete) month, only include days up to today (UTC); never future.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var lastIncluded = end <= today ? end : (today < start ? start.AddDays(-1) : today);
        return (start, end, lastIncluded);
    }

    private static int IsoDay(DateOnly date)
    {
        var dow = (int)date.DayOfWeek;   // Sun=0..Sat=6
        return dow == 0 ? 7 : dow;       // 1=Mon..7=Sun
    }

    private static bool TryParseStatus(string? value, out EmployeeStatus status)
    {
        status = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return Enum.TryParse(value.Trim(), ignoreCase: true, out status);
    }

    private static string? NormalizeFormat(string? format)
    {
        var f = format?.Trim().ToLowerInvariant();
        return f switch
        {
            "csv" => "csv",
            "xlsx" or "excel" => "xlsx",
            "pdf" => "pdf",
            _ => null,
        };
    }

    // ══════════════════════════════════════════════════════════════
    //  Export rendering (FR-6/AC-4): CSV (no lib) + XLSX (ClosedXML) + PDF (QuestPDF)
    // ══════════════════════════════════════════════════════════════

    private static readonly string[] ExportColumns =
    {
        "Employee No", "Employee", "Department", "Present Days", "Absent Days", "Late", "Early Departure",
        "Work Hours", "Overtime Hours", "Leave Days", "Holidays", "Weekly Offs", "LOP Days",
    };

    public (byte[] Content, string FileName, string ContentType) RenderExport(
        int year, int month, string format, MonthlySummaryResult result)
    {
        var baseName = $"attendance-summary-{YearMonth(year, month)}";
        return format switch
        {
            "xlsx" => (RenderXlsx(result), $"{baseName}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            "pdf" => (RenderPdf(year, month, result), $"{baseName}.pdf", "application/pdf"),
            _ => (RenderCsv(result), $"{baseName}.csv", "text/csv"),
        };
    }

    private static string[] RowCells(EmployeeMonthlySummaryDto r) => new[]
    {
        r.EmployeeNumber ?? string.Empty,
        r.EmployeeName,
        r.DepartmentName ?? string.Empty,
        Num(r.PresentDays),
        Num(r.AbsentDays),
        r.LateCount.ToString(CultureInfo.InvariantCulture),
        r.EarlyDepartureCount.ToString(CultureInfo.InvariantCulture),
        Hours(r.WorkMinutes),
        Hours(r.OvertimeMinutes),
        Num(r.LeaveDays),
        r.Holidays.ToString(CultureInfo.InvariantCulture),
        r.WeeklyOffs.ToString(CultureInfo.InvariantCulture),
        Num(r.LopDays),
    };

    private static byte[] RenderCsv(MonthlySummaryResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", ExportColumns.Select(Csv)));
        foreach (var r in result.Rows)
            sb.AppendLine(string.Join(",", RowCells(r).Select(Csv)));
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] RenderXlsx(MonthlySummaryResult result)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Attendance Summary");

        for (int c = 0; c < ExportColumns.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = ExportColumns[c];
            cell.Style.Font.Bold = true;
        }

        int row = 2;
        foreach (var r in result.Rows)
        {
            var cells = RowCells(r);
            for (int c = 0; c < cells.Length; c++)
                ws.Cell(row, c + 1).Value = cells[c];
            row++;
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] RenderPdf(int year, int month, MonthlySummaryResult result)
    {
        // QuestPDF Community license (free for our use). Set once is idempotent.
        QuestPDF.Settings.License = LicenseType.Community;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(20);
                page.DefaultTextStyle(t => t.FontSize(8));

                page.Header().Text($"Monthly Attendance Summary — {YearMonth(year, month)}")
                    .FontSize(14).Bold();

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        foreach (var _ in ExportColumns)
                            cols.RelativeColumn();
                    });

                    foreach (var h in ExportColumns)
                        table.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text(h).Bold();

                    foreach (var r in result.Rows)
                        foreach (var cell in RowCells(r))
                            table.Cell().Padding(3).Text(cell);
                });

                page.Footer().AlignRight().Text(t =>
                {
                    t.Span("Generated ");
                    t.Span(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
                });
            });
        });

        return document.GeneratePdf();
    }

    private static string Csv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private static string Num(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Hours(int minutes) =>
        (minutes / 60m).ToString("0.##", CultureInfo.InvariantCulture);
}
