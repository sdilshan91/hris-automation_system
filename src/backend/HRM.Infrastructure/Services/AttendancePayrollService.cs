using System.Globalization;
using System.Text.Json;
using HRM.Application.Common.Helpers;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Attendance ⇄ Payroll integration — ATTENDANCE SIDE (US-ATT-009). See
/// <see cref="IAttendancePayrollService"/> for the contract and the scope boundary (no salary math).
///
/// REUSE: the core per-employee rollup (present/absent/lop/work/overtime) comes from
/// <see cref="IAttendanceSummaryService"/> (US-ATT-007), which already reconciles approved leave
/// (FR-7), approved-overtime-only (FR-8) and the late-deduction LOP contribution (BR-4). This service
/// augments each row with the scheduled working-days count, the late-deduction breakdown, and the
/// approved-overtime multiplier breakdown, and applies the BR-7 terminated-employee cutoff.
///
/// LOCK: the canonical <see cref="AttendancePeriodLock"/> (consolidated from the former US-ATT-003
/// PayrollLockPeriod placeholder) — lock/unlock with in-row + AuditInterceptor audit (FR-3/FR-4).
///
/// DEFERRED (no Payroll module — module 6): AC-2/AC-3 salary computations, FR-5 payroll-input columns,
/// FR-6 payroll-refresh trigger (a GetPayrollDataAsync re-pull always returns current data). BR-8
/// payroll cutoff defaults to the calendar month (tenant-configurable cutoff deferred).
/// </summary>
public sealed class AttendancePayrollService : IAttendancePayrollService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IAttendanceSummaryService _summaryService;
    private readonly IHolidayProvider? _holidayProvider;
    private readonly ILogger<AttendancePayrollService> _logger;

    public AttendancePayrollService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IAttendanceSummaryService summaryService,
        ILogger<AttendancePayrollService> logger,
        IHolidayProvider? holidayProvider = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _summaryService = summaryService;
        _logger = logger;
        // CAL-5: TRAILING-OPTIONAL so the existing fixtures that compose their own DI container keep resolving
        // (same pattern as OvertimeService's IHolidayProvider, IWorkflowRuntime and IAttendanceNotificationService).
        // DI always supplies the real HolidayProvider. When absent, no holiday set can be built, so the
        // holiday-exclusion policy cannot take effect — which coincides with the code-default (off) and is why
        // this is safe rather than a silent money hole. A fixture that means to exercise the ON path MUST pass one.
        _holidayProvider = holidayProvider;
    }

    // ══════════════════════════════════════════════════════════════
    //  FR-1/FR-2: payroll data pull
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<AttendancePayrollResult>> GetPayrollDataAsync(
        int year, int month, IReadOnlyList<Guid>? employeeIds, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<AttendancePayrollResult>.Failure("Tenant context is not resolved.", 400);
        if (month is < 1 or > 12)
            return Result<AttendancePayrollResult>.Failure("Month must be between 1 and 12.", 400, "invalid_month");

        var period = Period(year, month);

        // Reuse the US-ATT-007 monthly summary for the core rollup (present/absent/lop/work/overtime).
        // It includes ALL non-terminated employees plus generates on-demand if not yet materialized.
        var summary = await _summaryService.GetMonthlyAsync(
            year, month, new MonthlySummaryFilter(), cancellationToken);
        if (summary.IsFailure)
            return Result<AttendancePayrollResult>.Failure(summary.Error!, summary.StatusCode ?? 400);

        var summaryByEmp = summary.Value!.Rows.ToDictionary(r => r.EmployeeId);

        // BR-7: also surface TERMINATED employees who have attendance up to their last working day —
        // the summary excludes terminated employees, so add them with a date cap.
        var (monthStart, monthEnd) = MonthBounds(year, month);

        var requested = employeeIds is { Count: > 0 } ? employeeIds.ToHashSet() : null;

        var employeesQuery = _dbContext.Employees.AsNoTracking().AsQueryable();
        if (requested is not null)
            employeesQuery = employeesQuery.Where(e => requested.Contains(e.Id));
        var employees = await employeesQuery.OrderBy(e => e.EmployeeNo).ToListAsync(cancellationToken);

        // BR-7: last working day per terminated employee (most recent status_change → Terminated).
        var lastWorkingDays = await TerminatedLastWorkingDaysAsync(employees, cancellationToken);

        // Approved overtime in the period grouped by employee (FR-8) for the multiplier breakdown.
        var overtimeByEmp = await ApprovedOvertimeByEmployeeAsync(monthStart, monthEnd, cancellationToken);

        // ISSUE-090: employees with ANY real attendance record in the period. A non-terminated employee
        // with ZERO records would otherwise be fabricated as fully absent by the materialized monthly
        // summary (GetMonthlyAsync generates an all-absent row for every non-terminated employee). Such
        // employees are OMITTED below — "no data" is not "absent". Employees with partial data keep their
        // real present/absent computation.
        var tenantZone = await ResolveTenantTimeZoneAsync(cancellationToken);
        var employeesWithRecords = await EmployeesWithAttendanceRecordsAsync(monthStart, monthEnd, tenantZone, cancellationToken);

        // BUG-125: resolve every employee's working-weekday set in a FIXED number of queries (was a
        // per-employee 2-3-query N+1 via WorkingDaysAsync in the loop below). Shift selection is as-of
        // monthStart — identical to the former per-employee resolution — while each employee's own
        // effectiveEnd still bounds the in-memory day count.
        var workingDaySets = await ShiftScheduleResolver.ResolveWorkingDaySetsAsync(
            _dbContext, employees.Select(e => e.Id).ToList(), monthStart, cancellationToken);

        // CAL-5 (US-ATT-011 AC-4/FR-5): when the tenant's EFFECTIVE calendar policy at monthStart excludes public
        // holidays, a holiday is not a working day — so TotalWorkingDays (the payroll pro-ration DENOMINATOR, and
        // the divisor behind the LOP daily rate + the OT hourly rate) drops those dates. Holidays are
        // LOCATION-scoped, so the sets are resolved ONCE per DISTINCT Employee.LocationId (NOT per employee — this
        // loop covers every employee in the tenant). Flag off (the default, and every existing tenant) → the map
        // stays null, no holiday query runs, and every figure is byte-identical to the pre-CAL-5 engine.
        // `_holidayProvider is not null` short-circuits BEFORE the policy query: with no provider (a fixture that
        // composes its own container) no holiday set can be built, so the exclusion cannot apply — which
        // coincides with the code-default (off). DI always supplies the provider in production.
        var excludeHolidays = _holidayProvider is not null
            && await PayrollCalendarResolver.ExcludeHolidaysAsync(_dbContext, monthStart, cancellationToken);
        var holidaysByLocation = excludeHolidays
            ? await PayrollCalendarResolver.HolidaysByLocationAsync(
                _holidayProvider!, employees.Select(e => e.LocationId), monthStart, monthEnd, cancellationToken)
            : null;

        var rows = new List<AttendancePayrollRowDto>();
        foreach (var emp in employees)
        {
            // Determine the effective period end for this employee (BR-7 cutoff for terminated).
            DateOnly effectiveEnd = monthEnd;
            bool terminatedBeforeMonth = false;
            if (emp.Status == EmployeeStatus.Terminated)
            {
                if (lastWorkingDays.TryGetValue(emp.Id, out var lwd))
                {
                    if (lwd < monthStart) { terminatedBeforeMonth = true; }
                    else if (lwd < effectiveEnd) { effectiveEnd = lwd; }
                }
                // No status_change record → include the whole month (no cutoff signal available).
            }
            if (terminatedBeforeMonth)
                continue;   // terminated before this period — no payroll data.

            // ISSUE-090: a non-terminated employee with no attendance records for the period would be
            // reported as fully absent by the summary row. Omit them — no data ≠ absent. (Terminated
            // employees are handled by the BR-7 branch below, which never fabricates absent days.)
            if (emp.Status != EmployeeStatus.Terminated && !employeesWithRecords.Contains(emp.Id))
                continue;

            // CAL-5: the employee's LOCATION-scoped holiday set (null when the flag is off → unchanged count).
            int workingDays = ShiftScheduleResolver.CountWorkingDays(
                workingDaySets[emp.Id], monthStart, effectiveEnd,
                PayrollCalendarResolver.For(holidaysByLocation, emp.LocationId));

            // Core rollup from the summary (non-terminated). Terminated employees have no summary row
            // (they were excluded) — compute their minimal rollup from the overtime + a zeroed base;
            // present/absent/lop for terminated employees up to the cutoff are not re-derived here
            // (the summary is the source of truth for active staff; terminated-employee LOP is a
            // payroll edge handled when the cutoff applies). Document: terminated rows carry overtime
            // and working-days; present/absent/lop default to 0 absent the summary row.
            var ot = overtimeByEmp.GetValueOrDefault(emp.Id);
            int approvedOtMinutes = ot?.TotalMinutes ?? 0;
            var multiplierDetails = ot?.ByMultiplier ?? new Dictionary<string, int>();

            if (summaryByEmp.TryGetValue(emp.Id, out var s))
            {
                rows.Add(new AttendancePayrollRowDto
                {
                    EmployeeId = emp.Id,
                    Period = period,
                    TotalWorkingDays = workingDays,
                    TotalPresentDays = s.PresentDays,
                    TotalAbsentDays = s.AbsentDays,
                    LopDays = s.LopDays,
                    LateDeductionDays = LateDeductionPortion(s),
                    ApprovedOvertimeMinutes = approvedOtMinutes,
                    TotalWorkMinutes = s.WorkMinutes,
                    OvertimeMultiplierDetails = multiplierDetails,
                });
            }
            else if (emp.Status == EmployeeStatus.Terminated)
            {
                rows.Add(new AttendancePayrollRowDto
                {
                    EmployeeId = emp.Id,
                    Period = period,
                    TotalWorkingDays = workingDays,
                    TotalPresentDays = 0m,
                    TotalAbsentDays = 0m,
                    LopDays = 0m,
                    LateDeductionDays = 0m,
                    ApprovedOvertimeMinutes = approvedOtMinutes,
                    TotalWorkMinutes = 0,
                    OvertimeMultiplierDetails = multiplierDetails,
                });
            }
        }

        return Result<AttendancePayrollResult>.Success(new AttendancePayrollResult
        {
            Period = period,
            Rows = rows,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  §7 / AC-4 / AC-5: lock lifecycle
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<PeriodLockDto?>> GetPeriodLockAsync(
        int year, int month, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PeriodLockDto?>.Failure("Tenant context is not resolved.", 400);
        if (month is < 1 or > 12)
            return Result<PeriodLockDto?>.Failure("Month must be between 1 and 12.", 400, "invalid_month");

        var (monthStart, monthEnd) = MonthBounds(year, month);

        // The active lock whose range overlaps the month (banner/status).
        var locks = await _dbContext.AttendancePeriodLocks.AsNoTracking()
            .Where(p => p.IsLocked && p.PeriodStart <= monthEnd && p.PeriodEnd >= monthStart)
            .OrderByDescending(p => p.LockedAt)
            .ToListAsync(cancellationToken);

        var current = locks.FirstOrDefault();
        return Result<PeriodLockDto?>.Success(current is null ? null : MapLock(current));
    }

    public async Task<Result<PeriodLockDto>> LockPeriodAsync(
        DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PeriodLockDto>.Failure("Tenant context is not resolved.", 400);
        // ISSUE-088: reject an empty/missing body — an omitted request deserializes both bounds to
        // default(DateOnly) (0001-01-01), which previously slipped past the range check and created a garbage
        // lock. Both bounds must be real calendar dates (floor: year 2000).
        var minReasonableDate = new DateOnly(2000, 1, 1);
        if (periodStart < minReasonableDate || periodEnd < minReasonableDate)
            return Result<PeriodLockDto>.Failure(
                "A valid period start and end date are required.", 400, "invalid_period");
        if (periodEnd < periodStart)
            return Result<PeriodLockDto>.Failure(
                "Period end must be on or after period start.", 400, "invalid_range");

        // AC-4 / NFR-2: reject if an ACTIVE lock already covers any part of this range (atomic; the
        // partial unique index also guards the exact-range case at the DB level).
        var overlaps = await _dbContext.AttendancePeriodLocks
            .AnyAsync(p => p.IsLocked && p.PeriodStart <= periodEnd && p.PeriodEnd >= periodStart,
                cancellationToken);
        if (overlaps)
            return Result<PeriodLockDto>.Failure(
                "An overlapping attendance period is already locked.", 409, "already_locked");

        var now = DateTime.UtcNow;
        var actingUser = _currentUser.IsAuthenticated ? _currentUser.UserId : (Guid?)null;

        var entity = new AttendancePeriodLock
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            IsLocked = true,
            LockedBy = actingUser,
            LockedAt = now,
        };

        _dbContext.AttendancePeriodLocks.Add(entity);
        // ISSUE-089: queryable tenant audit row (after-snapshot) for the period LOCK, same transaction —
        // the AuditInterceptor only stamps created_at/updated_at, not a searchable audit_log row.
        AddPeriodLockAudit("AttendancePeriod.Locked", entity.Id, before: null, after: SnapshotLock(entity),
            detail: $"Attendance period {periodStart}..{periodEnd} locked.");
        // FR-4 / NFR-2: single atomic SaveChanges; the AuditInterceptor stamps created_at/created_by.
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Attendance period {Start}..{End} LOCKED by {User} (lock {LockId}, tenant {TenantId}).",
            periodStart, periodEnd, actingUser, entity.Id, _tenantContext.TenantId);

        return Result<PeriodLockDto>.Success(MapLock(entity));
    }

    public async Task<Result<PeriodLockDto>> UnlockPeriodAsync(
        Guid lockId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PeriodLockDto>.Failure("Tenant context is not resolved.", 400);

        var entity = await _dbContext.AttendancePeriodLocks
            .FirstOrDefaultAsync(p => p.Id == lockId, cancellationToken);
        if (entity is null)
            return Result<PeriodLockDto>.Failure("Lock not found.", 404);

        if (!entity.IsLocked)
            return Result<PeriodLockDto>.Failure(
                "This period is already unlocked.", 409, "already_unlocked");

        var now = DateTime.UtcNow;
        var actingUser = _currentUser.IsAuthenticated ? _currentUser.UserId : (Guid?)null;

        // ISSUE-089: snapshot the locked state before unlocking so the audit captures before/after.
        var beforeSnapshot = SnapshotLock(entity);

        entity.IsLocked = false;
        entity.UnlockedBy = actingUser;
        entity.UnlockedAt = now;

        // ISSUE-089: queryable tenant audit row (before/after) for the period UNLOCK, same transaction.
        AddPeriodLockAudit("AttendancePeriod.Unlocked", entity.Id, before: beforeSnapshot, after: SnapshotLock(entity),
            detail: $"Attendance period {entity.PeriodStart}..{entity.PeriodEnd} unlocked.");
        // FR-4: single atomic SaveChanges; the AuditInterceptor stamps updated_at/updated_by.
        await _dbContext.SaveChangesAsync(cancellationToken);

        // FR-6 (trigger payroll refresh): DEFERRED — no Payroll module to notify. A re-pull of
        // GetPayrollDataAsync after correcting attendance always returns current data (TODO US-PAY).
        _logger.LogInformation(
            "Attendance period {Start}..{End} UNLOCKED by {User} (lock {LockId}, tenant {TenantId}).",
            entity.PeriodStart, entity.PeriodEnd, actingUser, entity.Id, _tenantContext.TenantId);

        return Result<PeriodLockDto>.Success(MapLock(entity));
    }

    // ══════════════════════════════════════════════════════════════
    //  FR-5: reconciliation (attendance side)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<ReconciliationResult>> GetReconciliationAsync(
        int year, int month, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ReconciliationResult>.Failure("Tenant context is not resolved.", 400);
        if (month is < 1 or > 12)
            return Result<ReconciliationResult>.Failure("Month must be between 1 and 12.", 400, "invalid_month");

        var summary = await _summaryService.GetMonthlyAsync(
            year, month, new MonthlySummaryFilter(), cancellationToken);
        if (summary.IsFailure)
            return Result<ReconciliationResult>.Failure(summary.Error!, summary.StatusCode ?? 400);

        var rows = summary.Value!.Rows.Select(r => new ReconciliationRowDto
        {
            EmployeeId = r.EmployeeId,
            EmployeeName = r.EmployeeName,
            PresentDays = r.PresentDays,
            LopDays = r.LopDays,
            ApprovedOvertimeMinutes = r.OvertimeMinutes,
            TotalWorkMinutes = r.WorkMinutes,
        }).ToList();

        // FR-5 payroll-input columns are DEFERRED (no Payroll module) — attendance side only.
        return Result<ReconciliationResult>.Success(new ReconciliationResult
        {
            Period = Period(year, month),
            Rows = rows,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private sealed record OvertimeAgg(int TotalMinutes, Dictionary<string, int> ByMultiplier);

    /// <summary>
    /// ISSUE-065: resolves the tenant's configured time zone into a <see cref="TimeZoneInfo"/> (UTC
    /// fallback via <see cref="TenantClock.ResolveTimeZone"/>) so the month day-boundary used to detect
    /// real attendance records is derived in tenant-local terms. One DB read per payroll pull.
    /// </summary>
    private async Task<TimeZoneInfo> ResolveTenantTimeZoneAsync(CancellationToken ct)
    {
        var tzId = await _dbContext.Tenants.AsNoTracking()
            .Where(t => t.Id == _tenantContext.TenantId)
            .Select(t => t.TimeZone)
            .FirstOrDefaultAsync(ct);
        return TenantClock.ResolveTimeZone(tzId, _logger);
    }

    /// <summary>
    /// ISSUE-090: the set of employees with ANY real attendance record in the period — an attendance log,
    /// an approved leave overlapping the period, an approved regularization, or approved overtime. Used to
    /// distinguish a genuinely-empty month (no data) from a real, partially-attended one, so the payroll
    /// pull never fabricates a full-month-absent row for an employee who simply has no data yet.
    /// Tenant-scoped via the global query filter.
    /// </summary>
    private async Task<HashSet<Guid>> EmployeesWithAttendanceRecordsAsync(
        DateOnly monthStart, DateOnly monthEnd, TimeZoneInfo tenantZone, CancellationToken ct)
    {
        // ISSUE-065: the tenant-local month maps to this UTC window for the clock-in filter (UTC zone → no-op).
        var startUtc = TenantClock.LocalToUtc(monthStart, TimeOnly.MinValue, tenantZone);
        var endUtc = TenantClock.LocalToUtc(monthEnd.AddDays(1), TimeOnly.MinValue, tenantZone);

        var withLogs = await _dbContext.AttendanceLogs.AsNoTracking()
            .Where(a => a.ClockIn >= startUtc && a.ClockIn < endUtc)
            .Select(a => a.EmployeeId).Distinct().ToListAsync(ct);

        var withLeave = await _dbContext.LeaveRequests.AsNoTracking()
            .Where(lr => lr.Status == LeaveRequestStatus.Approved
                && lr.StartDate <= monthEnd && lr.EndDate >= monthStart)
            .Select(lr => lr.EmployeeId).Distinct().ToListAsync(ct);

        var withRegularizations = await _dbContext.AttendanceRegularizations.AsNoTracking()
            .Where(r => r.Status == RegularizationStatus.Approved
                && r.Date >= monthStart && r.Date <= monthEnd)
            .Select(r => r.EmployeeId).Distinct().ToListAsync(ct);

        var withOvertime = await _dbContext.OvertimeRecords.AsNoTracking()
            .Where(o => o.Status == OvertimeStatus.Approved
                && o.Date >= monthStart && o.Date <= monthEnd)
            .Select(o => o.EmployeeId).Distinct().ToListAsync(ct);

        var set = new HashSet<Guid>(withLogs);
        set.UnionWith(withLeave);
        set.UnionWith(withRegularizations);
        set.UnionWith(withOvertime);
        return set;
    }

    /// <summary>
    /// FR-8: approved overtime minutes per employee in the range, with a breakdown by multiplier rate.
    /// Uses approved_minutes (falling back to overtime_minutes) for APPROVED records only.
    /// </summary>
    private async Task<Dictionary<Guid, OvertimeAgg>> ApprovedOvertimeByEmployeeAsync(
        DateOnly start, DateOnly end, CancellationToken ct)
    {
        var rows = await _dbContext.OvertimeRecords.AsNoTracking()
            .Where(o => o.Status == OvertimeStatus.Approved && o.Date >= start && o.Date <= end)
            .Select(o => new { o.EmployeeId, o.Multiplier, Minutes = o.ApprovedMinutes ?? o.OvertimeMinutes })
            .ToListAsync(ct);

        return rows
            .GroupBy(o => o.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => new OvertimeAgg(
                    g.Sum(x => x.Minutes),
                    g.GroupBy(x => x.Multiplier)
                     .ToDictionary(
                         mg => mg.Key.ToString("0.##", CultureInfo.InvariantCulture),
                         mg => mg.Sum(x => x.Minutes))));
    }

    /// <summary>
    /// BR-7: the last working day (most recent status_change → Terminated EffectiveDate) for each
    /// terminated employee in the set. Employees without such a record are absent from the result.
    /// </summary>
    private async Task<Dictionary<Guid, DateOnly>> TerminatedLastWorkingDaysAsync(
        IReadOnlyList<Employee> employees, CancellationToken ct)
    {
        var terminatedIds = employees
            .Where(e => e.Status == EmployeeStatus.Terminated)
            .Select(e => e.Id)
            .ToHashSet();
        if (terminatedIds.Count == 0)
            return new Dictionary<Guid, DateOnly>();

        var history = await _dbContext.EmploymentHistories.AsNoTracking()
            .Where(h => terminatedIds.Contains(h.EmployeeId)
                && h.ChangeType == "status_change"
                && h.NewValue == "Terminated")
            .Select(h => new { h.EmployeeId, h.EffectiveDate })
            .ToListAsync(ct);

        return history
            .GroupBy(h => h.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => DateOnly.FromDateTime(g.Max(x => x.EffectiveDate)));
    }

    /// <summary>
    /// The late-deduction-days portion of the summary row's LOP. The summary's LopDays bakes in both
    /// unexcused absences AND the late-deduction days (BR-4). We surface the late portion as
    /// LopDays − AbsentDays-not-covered-by-leave; since the summary's absent days are already
    /// leave-net, the late portion is max(0, LopDays − AbsentDays). Half-day-leave absences also feed
    /// LOP at 0.5 and are included in AbsentDays, so this difference isolates the late contribution.
    /// </summary>
    private static decimal LateDeductionPortion(EmployeeMonthlySummaryDto s)
        => Math.Max(0m, s.LopDays - s.AbsentDays);

    // ── Audit (ISSUE-089) ──────────────────────────────────────────────
    private static readonly JsonSerializerOptions AuditJsonOptions = new() { WriteIndented = false };

    /// <summary>
    /// ISSUE-089: appends a queryable tenant audit row (with before/after JSON snapshots) to the shared
    /// audit_log table for the period lock/unlock. Tenant/actor stamped from context (the HR admin acting).
    /// Mirrors <c>LeaveTypeService.AddLeaveTypeAudit</c>; persisted by the caller's SaveChanges (same tx).
    /// </summary>
    private void AddPeriodLockAudit(string action, Guid? resourceId, string? before, string? after, string detail)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = action,
            Action = action,
            ResourceType = "AttendancePeriod",
            ResourceId = resourceId?.ToString(),
            Before = before,
            After = after,
            Detail = detail,
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static string SnapshotLock(AttendancePeriodLock e) => JsonSerializer.Serialize(new
    {
        e.PeriodStart,
        e.PeriodEnd,
        e.IsLocked,
        e.LockedBy,
        e.LockedAt,
        e.UnlockedBy,
        e.UnlockedAt,
    }, AuditJsonOptions);

    private static PeriodLockDto MapLock(AttendancePeriodLock e) => new()
    {
        Id = e.Id,
        PeriodStart = e.PeriodStart,
        PeriodEnd = e.PeriodEnd,
        IsLocked = e.IsLocked,
        LockedBy = e.LockedBy,
        LockedAt = e.LockedAt,
        UnlockedBy = e.UnlockedBy,
        UnlockedAt = e.UnlockedAt,
    };

    private static string Period(int year, int month) => $"{year:D4}-{month:D2}";

    private static (DateOnly Start, DateOnly End) MonthBounds(int year, int month)
    {
        var start = new DateOnly(year, month, 1);
        return (start, start.AddMonths(1).AddDays(-1));
    }
}
