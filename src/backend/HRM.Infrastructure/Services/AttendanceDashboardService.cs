using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using HRM.Application.Common.Helpers;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HRM.Infrastructure.Services;

/// <summary>
/// HR attendance dashboard + reports service (US-ATT-010). See <see cref="IAttendanceDashboardService"/>
/// for the full responsibility/deferral list. All queries run under the EF global query filter
/// (TenantId == tenant) — no PostgreSQL RLS (NFR-4 deferred, same as the rest of the module). KPIs/live
/// board/custom report aggregate the live source data; trends + department comparison read the
/// AttendanceMonthlySummary table (BR-5). Computations project straight to DTOs and avoid N+1 (NFR-3).
/// </summary>
public sealed class AttendanceDashboardService : IAttendanceDashboardService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IAttendanceSummaryService _summaryService;
    private readonly ILogger<AttendanceDashboardService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = null };

    public AttendanceDashboardService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IAttendanceSummaryService summaryService,
        ILogger<AttendanceDashboardService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _summaryService = summaryService;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  KPIs (AC-1/FR-1, BR-1/BR-2/BR-3/BR-4)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<DashboardKpiDto>> GetKpisAsync(
        DateOnly date, string scope, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<DashboardKpiDto>.Failure("Tenant context is not resolved.", 400);

        var scopeResult = await ResolveScopeEmployeesAsync(scope, cancellationToken);
        if (scopeResult.IsFailure)
            return Result<DashboardKpiDto>.Failure(scopeResult.Error!, scopeResult.StatusCode ?? 400, scopeResult.ErrorCode);

        var employees = scopeResult.Value!;
        var tenantZone = await ResolveTenantTimeZoneAsync(cancellationToken);
        var statuses = await ComputeStatusesAsync(employees, date, tenantZone, cancellationToken);

        // BR-1: expected headcount = active employees − full-day approved leave − holiday-at-location.
        int onLeave = statuses.Count(s => s.Status == "ON_LEAVE");
        int holiday = statuses.Count(s => s.Status == "HOLIDAY");
        int clockedIn = statuses.Count(s => s.Status == "CLOCKED_IN");
        int expected = employees.Count - onLeave - holiday;
        if (expected < 0) expected = 0;

        // Absent = expected but not clocked in (NOT on leave / holiday). Pending = same set (not yet in).
        int pending = expected - clockedIn;
        if (pending < 0) pending = 0;

        decimal pct = expected > 0
            ? Math.Round((decimal)clockedIn / expected * 100m, 2)
            : 0m;

        return Result<DashboardKpiDto>.Success(new DashboardKpiDto
        {
            Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ExpectedHeadcount = expected,
            ClockedIn = clockedIn,
            PendingClockIn = pending,
            OnLeave = onLeave,
            Absent = pending,
            AttendancePercent = pct,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Live board (AC-2/FR-2 — polling, SignalR deferred)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<LiveBoardResult>> GetLiveBoardAsync(
        DateOnly date, string scope, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LiveBoardResult>.Failure("Tenant context is not resolved.", 400);

        var scopeResult = await ResolveScopeEmployeesAsync(scope, cancellationToken);
        if (scopeResult.IsFailure)
            return Result<LiveBoardResult>.Failure(scopeResult.Error!, scopeResult.StatusCode ?? 400, scopeResult.ErrorCode);

        var employees = scopeResult.Value!;
        var tenantZone = await ResolveTenantTimeZoneAsync(cancellationToken);
        var statuses = await ComputeStatusesAsync(employees, date, tenantZone, cancellationToken);
        var deptNames = await DepartmentNamesAsync(employees, cancellationToken);

        var rows = statuses
            .Select(s =>
            {
                var emp = s.Employee;
                return new LiveBoardRowDto
                {
                    EmployeeId = emp.Id,
                    EmployeeName = FullName(emp),
                    EmployeeNumber = emp.EmployeeNo,
                    DepartmentName = deptNames.GetValueOrDefault(emp.DepartmentId),
                    Status = s.Status,
                    ClockInAt = s.ClockInAt,
                };
            })
            .OrderBy(r => r.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result<LiveBoardResult>.Success(new LiveBoardResult
        {
            Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Rows = rows,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Department comparison (AC-3/FR-3) — from the monthly summary
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<DeptComparisonResult>> GetDepartmentComparisonAsync(
        int year, int month, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<DeptComparisonResult>.Failure("Tenant context is not resolved.", 400);
        if (month is < 1 or > 12)
            return Result<DeptComparisonResult>.Failure("Month must be between 1 and 12.", 400, "invalid_month");

        // Reuse the materialized monthly summary (generates on-demand if absent, AC-3 of ATT-007).
        var summary = await _summaryService.GetMonthlyAsync(
            year, month, new MonthlySummaryFilter(), cancellationToken);
        if (summary.IsFailure)
            return Result<DeptComparisonResult>.Failure(summary.Error!, summary.StatusCode ?? 400);

        // Map each employee row -> its department, aggregate present/absent per department.
        var employees = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.Status != EmployeeStatus.Terminated)
            .Select(e => new { e.Id, e.DepartmentId })
            .ToListAsync(cancellationToken);
        var deptByEmp = employees.ToDictionary(e => e.Id, e => e.DepartmentId);

        var deptNames = (await _dbContext.Departments.AsNoTracking()
                .Select(d => new { d.Id, d.Name })
                .ToListAsync(cancellationToken))
            .ToDictionary(d => d.Id, d => d.Name);

        var agg = new Dictionary<Guid, (decimal Present, decimal Absent, HashSet<Guid> Emps)>();
        foreach (var row in summary.Value!.Rows)
        {
            if (!deptByEmp.TryGetValue(row.EmployeeId, out var deptId)) continue;
            if (!agg.TryGetValue(deptId, out var cur))
                cur = (0m, 0m, new HashSet<Guid>());
            cur.Present += row.PresentDays;
            cur.Absent += row.AbsentDays;
            cur.Emps.Add(row.EmployeeId);
            agg[deptId] = cur;
        }

        var rows = agg
            .Select(kvp =>
            {
                decimal denom = kvp.Value.Present + kvp.Value.Absent;
                decimal rate = denom > 0m ? Math.Round(kvp.Value.Present / denom * 100m, 2) : 0m;
                return new DeptComparisonRowDto
                {
                    DepartmentId = kvp.Key,
                    DepartmentName = deptNames.GetValueOrDefault(kvp.Key, "—"),
                    AttendanceRatePct = rate,
                    EmployeeCount = kvp.Value.Emps.Count,
                };
            })
            .OrderByDescending(r => r.AttendanceRatePct)
            .ThenBy(r => r.DepartmentName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result<DeptComparisonResult>.Success(new DeptComparisonResult
        {
            Month = $"{year:D4}-{month:D2}",
            Rows = rows,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Custom date-range report (AC-4/FR-4) + export (FR-5)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<CustomReportResult>> GetCustomReportAsync(
        DateOnly from, DateOnly to, CustomReportFilter filter, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CustomReportResult>.Failure("Tenant context is not resolved.", 400);
        if (to < from)
            return Result<CustomReportResult>.Failure("The 'to' date must be on or after the 'from' date.", 400, "invalid_range");

        var rows = await ComputeCustomRowsAsync(from, to, filter, cancellationToken);

        return Result<CustomReportResult>.Success(new CustomReportResult
        {
            From = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            To = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Rows = rows,
        });
    }

    public async Task<Result<CustomReportExportResult>> ExportCustomReportAsync(
        DateOnly from, DateOnly to, string format, CustomReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CustomReportExportResult>.Failure("Tenant context is not resolved.", 400);

        var normalizedFormat = NormalizeFormat(format);
        if (normalizedFormat is null)
            return Result<CustomReportExportResult>.Failure(
                "Export format must be one of csv, xlsx, pdf.", 400, "invalid_format");

        var report = await GetCustomReportAsync(from, to, filter, cancellationToken);
        if (report.IsFailure)
            return Result<CustomReportExportResult>.Failure(report.Error!, report.StatusCode ?? 400, report.ErrorCode);

        var (content, fileName, contentType) = RenderExport(from, to, normalizedFormat, report.Value!);
        return Result<CustomReportExportResult>.Success(new CustomReportExportResult
        {
            FileContent = content,
            FileName = fileName,
            ContentType = contentType,
            RowCount = report.Value!.Rows.Count,
        });
    }

    private async Task<List<CustomReportRowDto>> ComputeCustomRowsAsync(
        DateOnly from, DateOnly to, CustomReportFilter filter, CancellationToken ct)
    {
        var tenantZone = await ResolveTenantTimeZoneAsync(ct);

        // Candidate employees (filters: department/location/shift).
        IQueryable<Employee> q = _dbContext.Employees.AsNoTracking()
            .Where(e => e.Status != EmployeeStatus.Terminated);
        if (filter.DepartmentId is { } dept) q = q.Where(e => e.DepartmentId == dept);
        if (filter.LocationId is { } loc) q = q.Where(e => e.LocationId == loc);
        var employees = await q.ToListAsync(ct);

        if (filter.ShiftId is { } shiftId)
        {
            var today = TenantClock.TodayIn(tenantZone);
            var assigned = (await _dbContext.EmployeeShifts.AsNoTracking()
                    .Where(es => es.ShiftId == shiftId && es.EffectiveFrom <= today
                        && (es.EffectiveTo == null || es.EffectiveTo >= today))
                    .Select(es => es.EmployeeId)
                    .ToListAsync(ct))
                .ToHashSet();
            employees = employees.Where(e => assigned.Contains(e.Id)).ToList();
        }

        var empIds = employees.Select(e => e.Id).ToHashSet();
        var deptNames = await DepartmentNamesAsync(employees, ct);

        // ISSUE-065: the tenant-local range maps to this UTC window (UTC zone → no-op).
        var rangeStart = TenantClock.LocalToUtc(from, TimeOnly.MinValue, tenantZone);
        var rangeEnd = TenantClock.LocalToUtc(to.AddDays(1), TimeOnly.MinValue, tenantZone);

        // Attendance logs in the range (present/late/work minutes per employee).
        var logs = await _dbContext.AttendanceLogs.AsNoTracking()
            .Where(a => empIds.Contains(a.EmployeeId) && a.ClockIn >= rangeStart && a.ClockIn < rangeEnd)
            .Select(a => new
            {
                a.EmployeeId,
                Day = a.ClockIn,
                a.IsLate,
                a.TotalWorkMinutes,
            })
            .ToListAsync(ct);

        // Approved overtime in the range.
        var overtime = await _dbContext.OvertimeRecords.AsNoTracking()
            .Where(o => empIds.Contains(o.EmployeeId) && o.Status == OvertimeStatus.Approved
                && o.Date >= from && o.Date <= to)
            .Select(o => new { o.EmployeeId, Minutes = o.ApprovedMinutes ?? o.OvertimeMinutes })
            .ToListAsync(ct);
        var otByEmp = overtime.GroupBy(o => o.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Minutes));

        // Distinct present DAYS + late count + work minutes per employee.
        var byEmp = logs
            .GroupBy(l => l.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => (
                    PresentDays: g.Select(x => TenantClock.LocalDateOf(x.Day, tenantZone)).Distinct().Count(),
                    LateCount: g.Count(x => x.IsLate),
                    WorkMinutes: g.Sum(x => x.TotalWorkMinutes ?? 0)));

        // Approved full-day leave days per employee in the range (counts toward neither present nor absent).
        var leaveByEmp = await ApprovedLeaveDaysByEmployeeAsync(empIds, from, to, ct);

        // Scheduled working days per employee over the range (for absent = scheduled − present − leave).
        var rows = new List<CustomReportRowDto>();
        foreach (var emp in employees)
        {
            int present = byEmp.TryGetValue(emp.Id, out var a) ? a.PresentDays : 0;
            int late = byEmp.TryGetValue(emp.Id, out var b) ? b.LateCount : 0;
            int work = byEmp.TryGetValue(emp.Id, out var c) ? c.WorkMinutes : 0;
            decimal leave = leaveByEmp.GetValueOrDefault(emp.Id, 0m);

            int scheduled = await ScheduledWorkingDaysAsync(emp.Id, from, to, ct);
            decimal absent = scheduled - present - leave;
            if (absent < 0m) absent = 0m;

            var row = new CustomReportRowDto
            {
                EmployeeId = emp.Id,
                EmployeeName = FullName(emp),
                EmployeeNumber = emp.EmployeeNo,
                DepartmentName = deptNames.GetValueOrDefault(emp.DepartmentId),
                PresentDays = present,
                AbsentDays = absent,
                LateCount = late,
                OvertimeMinutes = otByEmp.GetValueOrDefault(emp.Id, 0),
                WorkMinutes = work,
            };

            // AC-4 optional status filter: keep only rows matching the requested attendance status.
            if (MatchesStatusFilter(row, filter.Status))
                rows.Add(row);
        }

        return rows
            .OrderBy(r => r.EmployeeNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool MatchesStatusFilter(CustomReportRowDto row, string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return true;
        return status.Trim().ToUpperInvariant() switch
        {
            "PRESENT" => row.PresentDays > 0m,
            "ABSENT" => row.AbsentDays > 0m,
            "LATE" => row.LateCount > 0,
            _ => true,
        };
    }

    // ══════════════════════════════════════════════════════════════
    //  Trends (AC-5/FR-6/BR-5) — from the monthly summary table
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<TrendsResult>> GetTrendsAsync(int months, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<TrendsResult>.Failure("Tenant context is not resolved.", 400);

        int window = months is >= 1 and <= 36 ? months : 12;

        // The last `window` months ending with the current tenant-local month (ISSUE-065; UTC zone → no-op).
        var tenantZone = await ResolveTenantTimeZoneAsync(cancellationToken);
        var today = TenantClock.TodayIn(tenantZone);
        var periods = new List<string>();
        var anchor = new DateOnly(today.Year, today.Month, 1);
        for (int i = window - 1; i >= 0; i--)
            periods.Add(anchor.AddMonths(-i).ToString("yyyy-MM", CultureInfo.InvariantCulture));

        var periodSet = periods.ToHashSet();

        // BR-5: read the materialized monthly summary rows for these periods (tenant-scoped).
        var summaries = await _dbContext.AttendanceMonthlySummaries.AsNoTracking()
            .Where(s => periodSet.Contains(s.YearMonth))
            .Select(s => new
            {
                s.YearMonth,
                s.TotalPresentDays,
                s.TotalAbsentDays,
                s.TotalLateCount,
                s.TotalOvertimeMinutes,
            })
            .ToListAsync(cancellationToken);

        var byPeriod = summaries.GroupBy(s => s.YearMonth).ToDictionary(g => g.Key, g => g.ToList());

        var attendanceRate = new List<TrendPointDto>();
        var lateArrivals = new List<TrendPointDto>();
        var overtimeHours = new List<TrendPointDto>();
        var absenteeismRate = new List<TrendPointDto>();

        foreach (var period in periods)
        {
            var rows = byPeriod.GetValueOrDefault(period) ?? [];
            decimal present = rows.Sum(r => r.TotalPresentDays);
            decimal absent = rows.Sum(r => r.TotalAbsentDays);
            decimal denom = present + absent;

            decimal rate = denom > 0m ? Math.Round(present / denom * 100m, 2) : 0m;
            decimal absenteeism = denom > 0m ? Math.Round(absent / denom * 100m, 2) : 0m;
            decimal late = rows.Sum(r => r.TotalLateCount);
            decimal otHours = Math.Round(rows.Sum(r => r.TotalOvertimeMinutes) / 60m, 2);

            attendanceRate.Add(new TrendPointDto { Period = period, Value = rate });
            lateArrivals.Add(new TrendPointDto { Period = period, Value = late });
            overtimeHours.Add(new TrendPointDto { Period = period, Value = otHours });
            absenteeismRate.Add(new TrendPointDto { Period = period, Value = absenteeism });
        }

        return Result<TrendsResult>.Success(new TrendsResult
        {
            AttendanceRate = attendanceRate,
            LateArrivals = lateArrivals,
            OvertimeHours = overtimeHours,
            AbsenteeismRate = absenteeismRate,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Scheduled report config CRUD (FR-8)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<IReadOnlyList<ScheduledReportConfigDto>>> GetScheduledReportsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<ScheduledReportConfigDto>>.Failure("Tenant context is not resolved.", 400);

        var configs = await _dbContext.ScheduledReportConfigs.AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        IReadOnlyList<ScheduledReportConfigDto> dtos = configs.Select(MapToDto).ToList();
        return Result<IReadOnlyList<ScheduledReportConfigDto>>.Success(dtos);
    }

    public async Task<Result<ScheduledReportConfigDto>> CreateScheduledReportAsync(
        ScheduledReportConfigDto dto, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ScheduledReportConfigDto>.Failure("Tenant context is not resolved.", 400);

        var validation = ValidateScheduledDto(dto, out var frequency, out var format, out var deliveryTime);
        if (validation is not null)
            return Result<ScheduledReportConfigDto>.Failure(validation.Value.Message, 400, validation.Value.Code);

        var entity = new ScheduledReportConfig
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            ReportType = dto.ReportType.Trim(),
            Frequency = frequency,
            Format = format,
            DeliveryTime = deliveryTime,
            FiltersJson = SerializeFilters(dto.Filters),
            Recipients = dto.Recipients.ToList(),
            IsActive = dto.IsActive,
        };

        _dbContext.ScheduledReportConfigs.Add(entity);
        // ISSUE-093: queryable tenant audit row (after-snapshot) for the config create, same transaction.
        AddScheduledReportAudit("ScheduledReport.Created", entity.Id, before: null, after: SnapshotConfig(entity),
            detail: $"Scheduled report '{entity.ReportType}' ({entity.Frequency}/{entity.Format}) created.");
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Scheduled report {Id} ({Type}/{Frequency}) created for tenant {TenantId}.",
            entity.Id, entity.ReportType, entity.Frequency, _tenantContext.TenantId);

        return Result<ScheduledReportConfigDto>.Success(MapToDto(entity));
    }

    public async Task<Result<ScheduledReportConfigDto>> UpdateScheduledReportAsync(
        Guid id, ScheduledReportConfigDto dto, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ScheduledReportConfigDto>.Failure("Tenant context is not resolved.", 400);

        var entity = await _dbContext.ScheduledReportConfigs
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (entity is null)
            return Result<ScheduledReportConfigDto>.Failure("Scheduled report not found.", 404, "not_found");

        var validation = ValidateScheduledDto(dto, out var frequency, out var format, out var deliveryTime);
        if (validation is not null)
            return Result<ScheduledReportConfigDto>.Failure(validation.Value.Message, 400, validation.Value.Code);

        // ISSUE-093: snapshot the pre-mutation state so the audit row captures before/after.
        var beforeSnapshot = SnapshotConfig(entity);

        entity.ReportType = dto.ReportType.Trim();
        entity.Frequency = frequency;
        entity.Format = format;
        entity.DeliveryTime = deliveryTime;
        entity.FiltersJson = SerializeFilters(dto.Filters);
        entity.Recipients = dto.Recipients.ToList();
        entity.IsActive = dto.IsActive;

        // ISSUE-093: queryable tenant audit row (before/after) for the config update, same transaction.
        AddScheduledReportAudit("ScheduledReport.Updated", entity.Id, before: beforeSnapshot, after: SnapshotConfig(entity),
            detail: $"Scheduled report '{entity.ReportType}' updated.");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<ScheduledReportConfigDto>.Success(MapToDto(entity));
    }

    public async Task<Result> DeleteScheduledReportAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var entity = await _dbContext.ScheduledReportConfigs
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (entity is null)
            return Result.Failure("Scheduled report not found.", 404, "not_found");

        var beforeSnapshot = SnapshotConfig(entity);
        entity.IsDeleted = true;
        // ISSUE-093: queryable tenant audit row (before-snapshot) for the config delete, same transaction.
        AddScheduledReportAudit("ScheduledReport.Deleted", entity.Id, before: beforeSnapshot, after: null,
            detail: $"Scheduled report '{entity.ReportType}' deleted.");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ══════════════════════════════════════════════════════════════
    //  Shared status computation (KPIs + live board)
    // ══════════════════════════════════════════════════════════════

    private readonly record struct EmployeeDayStatus(Employee Employee, string Status, DateTime? ClockInAt);

    /// <summary>
    /// Classifies every employee's current status for the date. Precedence: HOLIDAY (at the employee's
    /// location) → ON_LEAVE (full-day approved leave) → CLOCKED_IN (a log exists for the date) →
    /// NOT_CLOCKED_IN. Loads all source data in 3 bulk queries (NFR-3 — no per-employee round-trips).
    /// </summary>
    private async Task<List<EmployeeDayStatus>> ComputeStatusesAsync(
        List<Employee> employees, DateOnly date, TimeZoneInfo tenantZone, CancellationToken ct)
    {
        if (employees.Count == 0) return [];

        var empIds = employees.Select(e => e.Id).ToHashSet();
        // ISSUE-065: the tenant-local day [date 00:00, date+1 00:00) maps to this UTC window (UTC zone → no-op).
        var dayStart = TenantClock.LocalToUtc(date, TimeOnly.MinValue, tenantZone);
        var dayEnd = TenantClock.LocalToUtc(date.AddDays(1), TimeOnly.MinValue, tenantZone);

        // Clock-ins for the date (earliest clock-in per employee).
        var logs = await _dbContext.AttendanceLogs.AsNoTracking()
            .Where(a => empIds.Contains(a.EmployeeId) && a.ClockIn >= dayStart && a.ClockIn < dayEnd)
            .Select(a => new { a.EmployeeId, a.ClockIn })
            .ToListAsync(ct);
        var clockInByEmp = logs
            .GroupBy(l => l.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Min(x => x.ClockIn));

        // Full-day approved leave covering the date.
        var leaveEmpIds = (await _dbContext.LeaveRequests.AsNoTracking()
                .Where(lr => empIds.Contains(lr.EmployeeId)
                    && lr.Status == LeaveRequestStatus.Approved
                    && !lr.IsHalfDay
                    && lr.StartDate <= date && lr.EndDate >= date)
                .Select(lr => lr.EmployeeId)
                .ToListAsync(ct))
            .ToHashSet();

        // Public holidays on the date (tenant-wide or location-specific).
        var holidayLocations = await _dbContext.Holidays.AsNoTracking()
            .Where(h => h.IsActive && h.Type == HolidayType.Public && h.Date == date)
            .Select(h => h.LocationId)
            .ToListAsync(ct);
        bool tenantWideHoliday = holidayLocations.Any(l => l == null);
        var holidayLocationSet = holidayLocations.Where(l => l != null).Select(l => l!.Value).ToHashSet();

        var result = new List<EmployeeDayStatus>(employees.Count);
        foreach (var emp in employees)
        {
            bool isHoliday = tenantWideHoliday
                || (emp.LocationId is { } loc && holidayLocationSet.Contains(loc));

            string status;
            DateTime? clockInAt = null;
            if (clockInByEmp.TryGetValue(emp.Id, out var ci))
            {
                status = "CLOCKED_IN";
                clockInAt = ci;
            }
            else if (leaveEmpIds.Contains(emp.Id))
            {
                status = "ON_LEAVE";
            }
            else if (isHoliday)
            {
                status = "HOLIDAY";
            }
            else
            {
                status = "NOT_CLOCKED_IN";
            }

            result.Add(new EmployeeDayStatus(emp, status, clockInAt));
        }

        return result;
    }

    /// <summary>
    /// ISSUE-065: resolves the tenant's configured time zone into a <see cref="TimeZoneInfo"/> (UTC
    /// fallback via <see cref="TenantClock.ResolveTimeZone"/>) so day/month boundaries and "today" are
    /// derived in tenant-local terms. One DB read per dashboard operation.
    /// </summary>
    private async Task<TimeZoneInfo> ResolveTenantTimeZoneAsync(CancellationToken ct)
    {
        var tzId = await _dbContext.Tenants.AsNoTracking()
            .Where(t => t.Id == _tenantContext.TenantId)
            .Select(t => t.TimeZone)
            .FirstOrDefaultAsync(ct);
        return TenantClock.ResolveTimeZone(tzId, _logger);
    }

    // ══════════════════════════════════════════════════════════════
    //  Scope resolution (BR-3/BR-4)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves the candidate employee set for the requested scope. scope="all" requires the HR
    /// Attendance.View.All permission (BR-3); scope="team" narrows to the acting manager's direct
    /// reports (BR-4). Default scope is "all". All queries are tenant-scoped by the global filter.
    /// </summary>
    private async Task<Result<List<Employee>>> ResolveScopeEmployeesAsync(string scope, CancellationToken ct)
    {
        var normalized = (scope ?? "all").Trim().ToLowerInvariant();
        if (normalized is not ("all" or "team"))
            return Result<List<Employee>>.Failure("Scope must be 'all' or 'team'.", 400, "invalid_scope");

        if (normalized == "all")
        {
            if (!_currentUser.Permissions.Contains(PermissionCatalog.Attendance.ViewAll))
                return Result<List<Employee>>.Failure(
                    "You are not authorized to view the all-employees dashboard.", 403, "scope_not_allowed");

            var all = await _dbContext.Employees.AsNoTracking()
                .Where(e => e.Status != EmployeeStatus.Terminated)
                .ToListAsync(ct);
            return Result<List<Employee>>.Success(all);
        }

        // team: the acting manager's direct reports.
        var manager = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, ct);
        if (manager is null)
            return Result<List<Employee>>.Success([]);

        var team = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.ReportsToEmployeeId == manager.Id && e.Status != EmployeeStatus.Terminated)
            .ToListAsync(ct);
        return Result<List<Employee>>.Success(team);
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private async Task<Dictionary<Guid, decimal>> ApprovedLeaveDaysByEmployeeAsync(
        HashSet<Guid> empIds, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var requests = await _dbContext.LeaveRequests.AsNoTracking()
            .Where(lr => empIds.Contains(lr.EmployeeId)
                && lr.Status == LeaveRequestStatus.Approved
                && lr.StartDate <= to && lr.EndDate >= from)
            .Select(lr => new { lr.EmployeeId, lr.StartDate, lr.EndDate, lr.IsHalfDay })
            .ToListAsync(ct);

        var result = new Dictionary<Guid, decimal>();
        foreach (var r in requests)
        {
            var start = r.StartDate < from ? from : r.StartDate;
            var end = r.EndDate > to ? to : r.EndDate;
            decimal days = 0m;
            for (var d = start; d <= end; d = d.AddDays(1))
                days += (r.IsHalfDay && r.StartDate == r.EndDate) ? 0.5m : 1m;
            result[r.EmployeeId] = result.GetValueOrDefault(r.EmployeeId, 0m) + days;
        }
        return result;
    }

    /// <summary>
    /// Counts scheduled working days for an employee over the range, using the employee's effective (or
    /// default) shift working-days. No shift → every calendar day is treated as working (matches the
    /// summary service's "empty working-days = all days" rule).
    /// </summary>
    private async Task<int> ScheduledWorkingDaysAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var workingDays = await ResolveWorkingDaysAsync(employeeId, from, ct);

        int count = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (workingDays.Count == 0 || workingDays.Contains(IsoDay(d)))
                count++;
        }
        return count;
    }

    private async Task<HashSet<int>> ResolveWorkingDaysAsync(Guid employeeId, DateOnly date, CancellationToken ct)
    {
        var assignment = await _dbContext.EmployeeShifts.AsNoTracking()
            .Where(es => es.EmployeeId == employeeId
                && es.EffectiveFrom <= date
                && (es.EffectiveTo == null || es.EffectiveTo >= date))
            .OrderByDescending(es => es.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        Shift? shift = null;
        if (assignment is not null)
            shift = await _dbContext.Shifts.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == assignment.ShiftId, ct);
        shift ??= await _dbContext.Shifts.AsNoTracking()
            .FirstOrDefaultAsync(s => s.IsDefault, ct);

        return shift?.WorkingDays.ToHashSet() ?? new HashSet<int>();
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

    private static string FullName(Employee e) => $"{e.FirstName} {e.LastName}".Trim();

    private static int IsoDay(DateOnly date)
    {
        var dow = (int)date.DayOfWeek;   // Sun=0..Sat=6
        return dow == 0 ? 7 : dow;       // 1=Mon..7=Sun
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

    // ── Scheduled-config mapping / validation ──────────────────────────

    private static (string Message, string Code)? ValidateScheduledDto(
        ScheduledReportConfigDto dto, out string frequency, out string format, out TimeOnly deliveryTime)
    {
        frequency = (dto.Frequency ?? string.Empty).Trim().ToUpperInvariant();
        format = (dto.Format ?? string.Empty).Trim().ToUpperInvariant();
        deliveryTime = default;

        if (string.IsNullOrWhiteSpace(dto.ReportType))
            return ("Report type is required.", "invalid_report_type");
        if (frequency is not ("DAILY" or "WEEKLY" or "MONTHLY"))
            return ("Frequency must be DAILY, WEEKLY, or MONTHLY.", "invalid_frequency");
        if (format is not ("CSV" or "XLSX" or "PDF"))
            return ("Format must be CSV, XLSX, or PDF.", "invalid_format");
        if (!TimeOnly.TryParseExact(dto.DeliveryTime, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out deliveryTime))
            return ("Delivery time must be in the format HH:mm.", "invalid_delivery_time");

        return null;
    }

    private static string SerializeFilters(Dictionary<string, object?>? filters)
        => JsonSerializer.Serialize(filters ?? new Dictionary<string, object?>(), JsonOpts);

    private static Dictionary<string, object?> DeserializeFilters(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOpts) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    // ── Audit (ISSUE-093) ──────────────────────────────────────────────

    /// <summary>
    /// ISSUE-093: appends a queryable tenant audit row (with before/after JSON snapshots) to the shared
    /// audit_log table for scheduled-report-config create/update/delete. Tenant/actor stamped from context.
    /// Mirrors <c>LeaveTypeService.AddLeaveTypeAudit</c>; persisted by the caller's SaveChanges (same tx).
    /// </summary>
    private void AddScheduledReportAudit(string action, Guid? resourceId, string? before, string? after, string detail)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = action,
            Action = action,
            ResourceType = "ScheduledReport",
            ResourceId = resourceId?.ToString(),
            Before = before,
            After = after,
            Detail = detail,
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static string SnapshotConfig(ScheduledReportConfig c) => JsonSerializer.Serialize(new
    {
        c.ReportType,
        c.Frequency,
        c.Format,
        DeliveryTime = c.DeliveryTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        c.Recipients,
        c.IsActive,
        c.FiltersJson,
    }, JsonOpts);

    private static ScheduledReportConfigDto MapToDto(ScheduledReportConfig c) => new()
    {
        Id = c.Id,
        ReportType = c.ReportType,
        Frequency = c.Frequency,
        Format = c.Format,
        DeliveryTime = c.DeliveryTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        Filters = DeserializeFilters(c.FiltersJson),
        Recipients = c.Recipients.ToList(),
        IsActive = c.IsActive,
    };

    // ══════════════════════════════════════════════════════════════
    //  Custom-report export rendering (FR-5): CSV + XLSX (ClosedXML) + PDF (QuestPDF)
    // ══════════════════════════════════════════════════════════════

    private static readonly string[] ExportColumns =
    {
        "Employee No", "Employee", "Department", "Present Days", "Absent Days", "Late",
        "Overtime Hours", "Work Hours",
    };

    private static (byte[] Content, string FileName, string ContentType) RenderExport(
        DateOnly from, DateOnly to, string format, CustomReportResult result)
    {
        var baseName = $"attendance-report-{from:yyyyMMdd}-{to:yyyyMMdd}";
        return format switch
        {
            "xlsx" => (RenderXlsx(result), $"{baseName}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            "pdf" => (RenderPdf(from, to, result), $"{baseName}.pdf", "application/pdf"),
            _ => (RenderCsv(result), $"{baseName}.csv", "text/csv"),
        };
    }

    private static string[] RowCells(CustomReportRowDto r) => new[]
    {
        r.EmployeeNumber ?? string.Empty,
        r.EmployeeName,
        r.DepartmentName ?? string.Empty,
        Num(r.PresentDays),
        Num(r.AbsentDays),
        r.LateCount.ToString(CultureInfo.InvariantCulture),
        Hours(r.OvertimeMinutes),
        Hours(r.WorkMinutes),
    };

    private static byte[] RenderCsv(CustomReportResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", ExportColumns.Select(Csv)));
        foreach (var r in result.Rows)
            sb.AppendLine(string.Join(",", RowCells(r).Select(Csv)));
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] RenderXlsx(CustomReportResult result)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Attendance Report");

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

    private static byte[] RenderPdf(DateOnly from, DateOnly to, CustomReportResult result)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(20);
                page.DefaultTextStyle(t => t.FontSize(8));

                page.Header().Text($"Attendance Report — {from:yyyy-MM-dd} to {to:yyyy-MM-dd}")
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
