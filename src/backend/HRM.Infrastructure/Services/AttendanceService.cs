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
using Npgsql;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Employee attendance punches (US-ATT-001).
/// All queries are tenant-scoped via ITenantContext and EF global query filters. Tenant isolation
/// (NFR-2 in the story asks for PostgreSQL RLS, which this codebase does NOT use) is enforced via
/// the global query filter + TenantInterceptor instead. Dashboard caching (FR-6, Redis) is deferred.
/// </summary>
public sealed class AttendanceService : IAttendanceService
{
    private const string OperationName = "ClockIn";

    // NFR-4: idempotency window for double-click protection.
    private static readonly TimeSpan IdempotencyWindow = TimeSpan.FromSeconds(5);

    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IOvertimeService _overtimeService;
    private readonly IShiftService _shiftService;
    private readonly IWorkflowRuntime? _workflowRuntime;
    private readonly IAttendanceNotificationService? _notifications;
    private readonly ILateEarlyService? _lateEarly;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AttendanceService> _logger;

    public AttendanceService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IOvertimeService overtimeService,
        IShiftService shiftService,
        ILogger<AttendanceService> logger,
        IWorkflowRuntime? workflowRuntime = null,
        IAttendanceNotificationService? notifications = null,
        ILateEarlyService? lateEarly = null,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _overtimeService = overtimeService;
        _shiftService = shiftService;
        // US-ADM-011c FR-11: optional so existing US-ATT unit tests keep compiling; DI always supplies it. When
        // present AND the tenant has an Active Attendance workflow definition, a submitted regularization routes
        // through the generic runtime; otherwise the legacy single-level manager approval runs (AC-11).
        _workflowRuntime = workflowRuntime;
        // US-NTF-006 Phase 6: optional so existing US-ATT unit tests keep compiling; DI always supplies the Real impl.
        _notifications = notifications;
        // US-ATT-008 FR-7: optional so existing US-ATT unit tests keep compiling; DI always supplies it. Used to
        // count distinct late days in the current calendar month for the chronic-lateness escalation crossing.
        _lateEarly = lateEarly;
        // DF-43: injectable clock so date-dependent logic (late detection, the FR-7 chronic-lateness month
        // window, period locks, the idempotency window) is deterministically testable. Trailing-optional +
        // TimeProvider.System default so existing US-ATT unit-test constructions keep compiling; DI supplies it.
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    /// <summary>DF-43: the current UTC instant via the injected clock (seam for date-dependent logic + tests).</summary>
    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public async Task<Result<AttendanceLogDto>> ClockInAsync(
        ClockInData data, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<AttendanceLogDto>.Failure("Tenant context is not resolved.", 400);

        // NFR-4: short-window idempotency. A replay within the window returns the original response.
        if (!string.IsNullOrWhiteSpace(data.IdempotencyKey))
        {
            var existing = await _dbContext.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.IdempotencyKey == data.IdempotencyKey && r.OperationName == OperationName,
                    cancellationToken);

            if (existing is not null && existing.ResponseJson is not null)
            {
                var cached = JsonSerializer.Deserialize<AttendanceLogDto>(existing.ResponseJson);
                if (cached is not null)
                    return Result<AttendanceLogDto>.Success(cached);
            }
        }

        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);
        if (employee is null)
            return Result<AttendanceLogDto>.Failure(
                "No employee record is linked to the current user.", 403);

        // BR-5: only active employees may clock in.
        if (employee.Status is EmployeeStatus.Terminated or EmployeeStatus.Inactive)
            return Result<AttendanceLogDto>.Failure(
                "Clock-in is not allowed for your current employment status.", 403);

        // BR-1 / FR-2 / AC-2: at most one open (un-clocked-out) record at any time.
        var hasOpen = await _dbContext.AttendanceLogs
            .AnyAsync(a => a.EmployeeId == employee.Id && a.ClockOut == null, cancellationToken);
        if (hasOpen)
            return Result<AttendanceLogDto>.Failure(
                "You have already clocked in. Please clock out first.", 409, "already_clocked_in");

        // ISSUE-065 / Phase 2b: resolve the tenant's zone once for this operation so every derived date /
        // time-of-day below is computed in tenant-local terms (UTC fallback → no-op for UTC tenants).
        var tenantZone = await ResolveTenantTimeZoneAsync(cancellationToken);

        // US-ATT-009 AC-4: once HR locks the attendance period, no further clock-in is allowed for a
        // date in the locked range. "Today" is the tenant-local calendar day of the punch (ISSUE-065).
        var today = TenantClock.TodayIn(tenantZone);
        if (await IsDateLockedAsync(today, cancellationToken))
            return Result<AttendanceLogDto>.Failure(
                "This date falls within a locked payroll period. Please contact HR.",
                409, "payroll_period_locked");

        var settings = await GetOrCreateSettingsAsync(employee, cancellationToken);

        // AC-5 / FR-4: IP allowlist enforcement. Each entry is an exact IP or a CIDR range (ISSUE-066).
        if (settings.IpAllowlistEnabled)
        {
            var ip = data.IpAddress;
            if (string.IsNullOrWhiteSpace(ip) || !IpAllowlistMatcher.IsAllowed(ip, settings.IpAllowlist))
                return Result<AttendanceLogDto>.Failure(
                    "Clock-in is only allowed from authorized network locations.", 403, "ip_not_allowed");
        }

        var hasCoordinates = data.Latitude.HasValue && data.Longitude.HasValue;

        // AC-3 / BR-2: geolocation required but missing.
        if (settings.RequireGeolocation && !hasCoordinates)
            return Result<AttendanceLogDto>.Failure(
                "Location is required to clock in. Please enable location access and try again.", 400);

        // US-ATT-001 AC-6 / BR-8 (US-CHR-013): a REMOTE employee is exempt from the geo-fence — they have no
        // branch to be near. OnSite and Hybrid stay fully enforced (a hybrid employee is still expected at the
        // office on their office days, and the module has no which-days-are-office-days concept to tell them
        // apart). The exemption is the geo-fence radius ONLY: RequireGeolocation above, and the IP allowlist +
        // photo rules below, are separate business rules and still apply to a Remote employee.
        var geoFenceExempt = employee.WorkArrangement == WorkArrangement.Remote;

        // FR-3 / DF-23 / ISSUE-068: geo-fence radius check when enabled and coordinates were supplied.
        // Multi-location: pass when the punch is within ANY allowed location's own radius; a single allowed
        // location behaves byte-identically to the legacy single-center fence.
        if (!geoFenceExempt && settings.GeoFenceEnabled && hasCoordinates)
        {
            bool withinAllowed;
            if (settings.GeoFenceLocations.Count > 0)
                // any-match: pass when the punch is within ANY allowed location's own radius.
                withinAllowed = settings.GeoFenceLocations.Any(loc =>
                    HaversineMeters((double)loc.Latitude, (double)loc.Longitude,
                                    (double)data.Latitude!.Value, (double)data.Longitude!.Value) <= loc.RadiusMeters);
            else if (settings.GeoFenceLatitude.HasValue && settings.GeoFenceLongitude.HasValue)
                // Legacy single-center fallback (a row with no allowed locations but a scalar center) — TODAY'S behavior.
                withinAllowed = HaversineMeters(
                    (double)settings.GeoFenceLatitude.Value, (double)settings.GeoFenceLongitude.Value,
                    (double)data.Latitude!.Value, (double)data.Longitude!.Value) <= settings.GeoFenceRadiusMeters;
            else
                // Enabled but nothing configured → no restriction (matches today: check skipped when no lat/lng).
                withinAllowed = true;

            if (!withinAllowed)
                return Result<AttendanceLogDto>.Failure(
                    "You are outside the allowed clock-in area.", 403, "geo_fence_violation");
        }

        // BR-6: selfie photo required.
        if (settings.RequirePhoto && string.IsNullOrWhiteSpace(data.PhotoUrl))
            return Result<AttendanceLogDto>.Failure(
                "A photo is required to clock in.", 400);

        var source = string.IsNullOrWhiteSpace(data.Source) ? "WEB" : data.Source!;

        var log = new AttendanceLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = employee.Id,
            ClockIn = UtcNow,                   // FR-1 / FR-7: store in UTC (DF-43 clock seam).
            ClockInLatitude = hasCoordinates ? data.Latitude : null,
            ClockInLongitude = hasCoordinates ? data.Longitude : null,
            ClockInIp = data.IpAddress,          // FR-5.
            ClockInUserAgent = data.UserAgent,   // FR-5.
            ClockInPhotoUrl = string.IsNullOrWhiteSpace(data.PhotoUrl) ? null : data.PhotoUrl,
            Source = source,
        };

        // US-ATT-008 (FR-1/FR-3/NFR-1): inline late detection against the resolved shift. FLEXIBLE
        // shifts are exempt (BR-6/§10). Grace resolves shift → tenant default → 0 (BR-3). Early
        // departure is NOT evaluated here (no clock-out yet) — it is computed on clock-out.
        var shiftSignals = await ResolveShiftSignalsAsync(
            employee.Id, TenantClock.LocalDateOf(log.ClockIn, tenantZone), settings, cancellationToken);
        TimeOnly? expectedShiftStart = null;
        if (shiftSignals is { } ss)
        {
            expectedShiftStart = ss.Start;
            // ISSUE-065: the punch instant is stored in UTC, but the shift start is configured in the
            // tenant's local time zone — compare LOCAL wall-clock to local shift start (UTC fallback).
            var lateEarly = LateEarlyCalculator.Evaluate(
                clockInTime: TenantClock.LocalTimeOfDay(log.ClockIn, tenantZone),
                clockOutTime: null,
                shiftStart: ss.Start,
                shiftEnd: ss.End,
                gracePeriodMinutes: ss.Grace,
                workedMinutes: null,
                minimumMinutes: ss.MinimumMinutes);

            log.IsLate = lateEarly.IsLate;
            log.LateMinutes = lateEarly.LateMinutes;
            log.LateByMinutes = lateEarly.LateByMinutes;
        }

        _dbContext.AttendanceLogs.Add(log);
        // ISSUE-067: write a queryable tenant audit row (after-snapshot) for the clock-in, in the SAME
        // transaction as the punch — not just an ILogger line.
        AddAttendanceAudit("Attendance.ClockedIn", "Attendance", log.Id,
            before: null, after: SnapshotLog(log),
            detail: $"Clocked in at {log.ClockIn:o}"
                  + (hasCoordinates ? $" @ {log.ClockInLatitude},{log.ClockInLongitude}" : string.Empty));
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsOpenClockInViolation(ex))
        {
            // BUG-047: two concurrent clock-ins (or a double-submit) for the same employee raced the
            // BR-1 open-record check above and tripped the partial unique index. Return the same clean
            // 409 as the pre-check instead of letting a 23505 bubble up as an HTTP 500. The audit row
            // rolls back with the failed insert (ISSUE-067 — same transaction).
            _logger.LogWarning(ex,
                "Concurrent clock-in conflicted on the open-record unique index. "
                + "EmployeeId={EmployeeId}, TenantId={TenantId}",
                employee.Id, _tenantContext.TenantId);
            return Result<AttendanceLogDto>.Failure(
                "You have already clocked in. Please clock out first.", 409, "already_clocked_in");
        }

        // FR-5: notify the employee when this clock-in was marked late (US-NTF-006 Phase 6). The AC-4 deduction
        // FLAG is the persisted is_late field feeding the monthly summary; this delivers the "you were late" signal.
        // The service never throws, so a delivery failure cannot break the committed punch.
        if (log.IsLate && _notifications is not null)
        {
            await _notifications.NotifyLateAsync(
                employee.Id,
                TenantClock.LocalDateOf(log.ClockIn, tenantZone),
                TenantClock.LocalTimeOfDay(log.ClockIn, tenantZone),
                expectedShiftStart,
                cancellationToken);
        }

        // US-ATT-008 FR-7: escalate chronic lateness to the line manager + HR, ONCE, on the punch that first
        // pushes the employee's distinct late-day count for the calendar month ABOVE the tenant chronic threshold.
        if (log.IsLate && _notifications is not null && _lateEarly is not null)
        {
            var latePolicy = await _dbContext.LatePolicies.AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);
            var chronicThreshold = latePolicy?.ChronicThreshold ?? 0;

            if (chronicThreshold > 0)
            {
                var asOfLocalDate = TenantClock.LocalDateOf(log.ClockIn, tenantZone);

                // Month-to-date distinct late days INCLUDING this now-committed punch (shared CountLateEarly def).
                var mtdLateDays = await _lateEarly.CountLateDaysInMonthAsync(
                    employee.Id, asOfLocalDate, cancellationToken);

                // (b) fire only on the FIRST late day-record of this local day: a second late punch the same day
                // leaves the distinct-day count unchanged (still threshold+1), so without this guard it would
                // re-fire. Together with (a) mtdLateDays == threshold+1 this fires on exactly one punch per month.
                var firstLateOfDay = !await HasOtherLateLogOnLocalDateAsync(
                    employee.Id, log.Id, asOfLocalDate, tenantZone, cancellationToken);

                if (mtdLateDays == chronicThreshold + 1 && firstLateOfDay)
                {
                    await _notifications.NotifyChronicLatenessAsync(
                        employee.Id, mtdLateDays, chronicThreshold, asOfLocalDate, cancellationToken);
                }
            }
        }

        var dto = MapToDto(log);

        // NFR-4: record the idempotency key so a replay within the window is a no-op.
        if (!string.IsNullOrWhiteSpace(data.IdempotencyKey))
            await TryRecordIdempotencyAsync(data.IdempotencyKey!, dto, cancellationToken);

        _logger.LogInformation(
            "Employee {EmployeeId} clocked in (attendance_log {LogId}) for tenant {TenantId}.",
            employee.Id, log.Id, _tenantContext.TenantId);

        return Result<AttendanceLogDto>.Success(dto);
    }

    public async Task<Result<ClockOutResultDto>> ClockOutAsync(
        ClockOutData data, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ClockOutResultDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);
        if (employee is null)
            return Result<ClockOutResultDto>.Failure(
                "No employee record is linked to the current user.", 403);

        // BR-1 / AC-2: clock-out requires an open (un-clocked-out) record.
        var openLog = await _dbContext.AttendanceLogs
            .Where(a => a.EmployeeId == employee.Id && a.ClockOut == null)
            .OrderByDescending(a => a.ClockIn)
            .FirstOrDefaultAsync(cancellationToken);
        if (openLog is null)
            return Result<ClockOutResultDto>.Failure(
                "No active clock-in found. Please clock in first or submit a regularization request.",
                404, "no_active_clock_in");

        // ISSUE-065 / Phase 2b: resolve the tenant's zone once so the lock-date, shift-resolution date,
        // and late/early comparison below are all in tenant-local terms (UTC fallback → no-op).
        var tenantZone = await ResolveTenantTimeZoneAsync(cancellationToken);

        // US-ATT-009 AC-4: a locked period freezes the day's record — no clock-out modification is
        // allowed once the open record's clock-in LOCAL date is in a locked range.
        if (await IsDateLockedAsync(TenantClock.LocalDateOf(openLog.ClockIn, tenantZone), cancellationToken))
            return Result<ClockOutResultDto>.Failure(
                "This date falls within a locked payroll period. Please contact HR.",
                409, "payroll_period_locked");

        var settings = await GetOrCreateSettingsAsync(employee, cancellationToken);

        var hasCoordinates = data.Latitude.HasValue && data.Longitude.HasValue;

        // AC-5 / FR-6: geolocation required by tenant policy but missing on clock-out.
        if (settings.RequireGeolocation && !hasCoordinates)
            return Result<ClockOutResultDto>.Failure(
                "Location is required to clock out. Please enable location access and try again.", 400);

        var clockOut = UtcNow;                           // FR-1: server-side UTC, not client-reported (DF-43 clock seam).

        // FR-2/FR-3/FR-4/FR-7, BR-2/BR-3/BR-4/BR-6: minute-accurate work-hours calculation.
        var calc = AttendanceCalculator.Calculate(openLog.ClockIn, clockOut, settings);

        // ISSUE-069: snapshot the pre-clock-out state so the audit row captures before/after.
        var beforeSnapshot = SnapshotLog(openLog);

        openLog.ClockOut = clockOut;
        openLog.ClockOutLatitude = hasCoordinates ? data.Latitude : null;
        openLog.ClockOutLongitude = hasCoordinates ? data.Longitude : null;
        openLog.ClockOutIp = data.IpAddress;
        openLog.TotalWorkMinutes = calc.TotalWorkMinutes;
        openLog.OvertimeMinutes = calc.OvertimeMinutes;
        openLog.Status = calc.Status;

        // US-ATT-008 (FR-2/FR-3/NFR-1): inline early-departure detection against the resolved shift.
        // Early departure = clock-out before shift end AND minimum hours unmet (BR-2). FLEXIBLE shifts
        // are exempt (BR-6/§10); grace does NOT apply to early departure (§10). Re-evaluate the late
        // flag too (idempotent — the clock-in value is preserved) against the same resolved shift.
        var shiftSignals = await ResolveShiftSignalsAsync(
            openLog.EmployeeId, TenantClock.LocalDateOf(openLog.ClockIn, tenantZone), settings, cancellationToken);
        if (shiftSignals is { } ss)
        {
            // ISSUE-065: compare LOCAL wall-clock times (tenant time zone) to the local shift start/end.
            // Both instants are stored in UTC; only the late/early comparison uses local time (UTC fallback).
            var lateEarly = LateEarlyCalculator.Evaluate(
                clockInTime: TenantClock.LocalTimeOfDay(openLog.ClockIn, tenantZone),
                clockOutTime: TenantClock.LocalTimeOfDay(clockOut, tenantZone),
                shiftStart: ss.Start,
                shiftEnd: ss.End,
                gracePeriodMinutes: ss.Grace,
                workedMinutes: calc.TotalWorkMinutes,
                minimumMinutes: ss.MinimumMinutes);

            openLog.IsLate = lateEarly.IsLate;
            openLog.LateMinutes = lateEarly.LateMinutes;
            openLog.LateByMinutes = lateEarly.LateByMinutes;
            openLog.IsEarlyDeparture = lateEarly.IsEarlyDeparture;
            openLog.EarlyDepartureMinutes = lateEarly.EarlyDepartureMinutes;
        }

        // US-ATT-006 (AC-1/FR-1/NFR-1): create the persistent overtime record as part of the SAME
        // clock-out transaction (no extra API call). The shift-standard baseline (US-ATT-005), the
        // weekday/weekend/holiday multiplier (BR-3/BR-7), the daily cap (BR-4) and the weekly-cap alert
        // (BR-5) are resolved in OvertimeService; returns null when no overtime is recognized.
        var overtimeRecord = await _overtimeService.BuildAutoDetectedAsync(
            openLog, employee, settings, calc.TotalWorkMinutes, cancellationToken);
        if (overtimeRecord is not null)
            _dbContext.OvertimeRecords.Add(overtimeRecord);

        // ISSUE-069: queryable tenant audit row (before/after) for the clock-out, in the SAME transaction.
        AddAttendanceAudit("Attendance.ClockedOut", "Attendance", openLog.Id,
            before: beforeSnapshot, after: SnapshotLog(openLog),
            detail: $"Clocked out at {clockOut:o} ({calc.TotalWorkMinutes} min worked).");

        // NFR-3 / NFR-1: single atomic SaveChanges (closed log + overtime record together);
        // the AuditInterceptor stamps updated_at/updated_by.
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Employee {EmployeeId} clocked out (attendance_log {LogId}, {TotalMinutes} min, status {Status}) for tenant {TenantId}.",
            employee.Id, openLog.Id, calc.TotalWorkMinutes, calc.Status, _tenantContext.TenantId);

        return Result<ClockOutResultDto>.Success(MapToResult(openLog));
    }

    public async Task<Result<ClockStatusDto>> GetClockStatusAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ClockStatusDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);
        if (employee is null)
            return Result<ClockStatusDto>.Failure(
                "No employee record is linked to the current user.", 403);

        // BR-1: the open (un-clocked-out) record, if any, drives IsClockedIn / ClockedInAt.
        var openLog = await _dbContext.AttendanceLogs
            .AsNoTracking()
            .Where(a => a.EmployeeId == employee.Id && a.ClockOut == null)
            .OrderByDescending(a => a.ClockIn)
            .FirstOrDefaultAsync(cancellationToken);

        // BR-2: read-only — default RequireGeolocation to false when no settings row exists.
        // Unlike clock-in, status must not create a settings row as a side effect — so this resolves
        // read-only and treats "no policy configured yet" as not-required (unchanged).
        // CAL-4: scoped to the employee's Location override, else the tenant default. An unpredicated read
        // would now pick an arbitrary row and could report another branch's geofence requirement.
        var effective = employee.LocationId is not null
            ? await _dbContext.AttendanceSettings.AsNoTracking()
                .FirstOrDefaultAsync(x => x.LocationId == employee.LocationId, cancellationToken)
            : null;
        effective ??= await AttendancePolicyResolver.GetTenantDefaultOrNullAsync(_dbContext, cancellationToken);
        var requireGeolocation = effective?.RequireGeolocation ?? false;

        // US-ATT-002: the most-recently-closed session, so the dashboard can re-render the summary
        // card after a reload. Independent of the open record above.
        var lastClosed = await _dbContext.AttendanceLogs
            .AsNoTracking()
            .Where(a => a.EmployeeId == employee.Id && a.ClockOut != null)
            .OrderByDescending(a => a.ClockOut)
            .FirstOrDefaultAsync(cancellationToken);

        var dto = new ClockStatusDto
        {
            IsClockedIn = openLog is not null,
            ClockedInAt = openLog?.ClockIn,
            RequireGeolocation = requireGeolocation,
            ShiftName = null,    // US-ATT-005 not built yet.
            ShiftStart = null,   // US-ATT-005 not built yet.
            LastCompleted = lastClosed is null ? null : MapToResult(lastClosed),
        };

        return Result<ClockStatusDto>.Success(dto);
    }

    public async Task<Result<RegularizationDto>> SubmitRegularizationAsync(
        SubmitRegularizationRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<RegularizationDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);
        if (employee is null)
            return Result<RegularizationDto>.Failure(
                "No employee record is linked to the current user.", 403);

        // Active-employee only (consistent with clock-in BR-5).
        if (employee.Status is EmployeeStatus.Terminated or EmployeeStatus.Inactive)
            return Result<RegularizationDto>.Failure(
                "Regularization is not allowed for your current employment status.", 403);

        var type = request.RegularizationType;

        // ISSUE-065 / Phase 2b: resolve the tenant's zone once for the day-boundary + wall-clock logic.
        var tenantZone = await ResolveTenantTimeZoneAsync(cancellationToken);

        // BR-4: no future dates. "Today" is the tenant-local calendar day (ISSUE-065; UTC fallback → no-op).
        var today = TenantClock.TodayIn(tenantZone);
        if (request.Date > today)
            return Result<RegularizationDto>.Failure(
                "Regularization requests cannot be submitted for future dates.", 400, "future_date");

        var settings = await GetOrCreateSettingsAsync(employee, cancellationToken);

        // AC-3 / FR-6 / BR-2: lookback window. The earliest allowed date is (today - (N-1)) so that
        // exactly N calendar days (including today) are eligible.
        var lookbackDays = settings.RegularizationLookbackDays;
        var earliestAllowed = today.AddDays(-(lookbackDays - 1));
        if (request.Date < earliestAllowed)
            return Result<RegularizationDto>.Failure(
                $"Regularization requests can only be submitted for the last {lookbackDays} days.",
                400, "lookback_exceeded");

        // US-ATT-009 AC-4 / US-ATT-003 AC-5 / FR-7 / BR-6: the date falls within an ACTIVE locked
        // attendance period (the canonical AttendancePeriodLock, formerly the PayrollLockPeriod
        // placeholder). Only IsLocked rows freeze their dates.
        var lockedPeriod = await _dbContext.AttendancePeriodLocks
            .AnyAsync(p => p.IsLocked && p.PeriodStart <= request.Date && p.PeriodEnd >= request.Date,
                cancellationToken);
        if (lockedPeriod)
            return Result<RegularizationDto>.Failure(
                "This date falls within a locked payroll period. Please contact HR.",
                409, "payroll_period_locked");

        // AC-4 / BR-3: at most one PENDING request per employee per date.
        var hasPending = await _dbContext.AttendanceRegularizations
            .AnyAsync(r => r.EmployeeId == employee.Id
                        && r.Date == request.Date
                        && r.Status == RegularizationStatus.Pending,
                      cancellationToken);
        if (hasPending)
            return Result<RegularizationDto>.Failure(
                "A pending regularization request already exists for this date.",
                409, "duplicate_pending");

        // AC-2 / BR-5: link to an existing attendance_log for the date if one exists. We do NOT
        // mutate the log here — that happens on approval (US-ATT-004). Match on the tenant-LOCAL calendar
        // day of clock_in (ISSUE-065): the local day [date 00:00, date+1 00:00) maps to this UTC window.
        var dayStart = TenantClock.LocalToUtc(request.Date, TimeOnly.MinValue, tenantZone);
        var dayEnd = TenantClock.LocalToUtc(request.Date.AddDays(1), TimeOnly.MinValue, tenantZone);
        var existingLog = await _dbContext.AttendanceLogs
            .Where(a => a.EmployeeId == employee.Id && a.ClockIn >= dayStart && a.ClockIn < dayEnd)
            .OrderByDescending(a => a.ClockIn)
            .FirstOrDefaultAsync(cancellationToken);

        var regularization = new AttendanceRegularization
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = employee.Id,
            AttendanceLogId = existingLog?.Id,
            Date = request.Date,
            RegularizationType = type,
            // Combine the regularized date + wall-clock "HH:mm" into the stored UTC timestamptz, interpreting
            // the wall-clock in the tenant's zone (ISSUE-065; UTC fallback → identical to the prior behavior).
            // Conditional on type, mirroring the validator's required-time rules.
            RequestedClockIn = RegularizationType.RequiresClockIn(type)
                ? request.CombineToUtc(request.RequestedClockIn, tenantZone) : null,
            RequestedClockOut = RegularizationType.RequiresClockOut(type)
                ? request.CombineToUtc(request.RequestedClockOut, tenantZone) : null,
            Reason = request.Reason.Trim(),
            Status = RegularizationStatus.Pending,
            WorkflowInstanceId = null,   // TODO(US-ADM-007): set when the Approval Workflow Engine lands.
        };

        _dbContext.AttendanceRegularizations.Add(regularization);

        // ISSUE-071: queryable tenant audit row (after-snapshot) for the regularization SUBMIT, same tx.
        AddAttendanceAudit("AttendanceRegularization.Submitted", "AttendanceRegularization", regularization.Id,
            before: null, after: SnapshotRegularization(regularization),
            detail: $"Regularization ({type}) submitted for {regularization.Date}.");

        // NFR-3: single SaveChanges; the AuditInterceptor stamps created_at/created_by.
        await _dbContext.SaveChangesAsync(cancellationToken);

        // US-ADM-011c FR-11/AC-11: if the tenant has an Active Attendance approval workflow, instantiate it now so
        // the regularization routes through the configured (multi-level/conditional) steps. No active definition →
        // WorkflowInstanceId stays null and the legacy single-level manager approval (US-ATT-004) governs it (AC-11).
        if (_workflowRuntime is not null)
        {
            var requestData = new Dictionary<string, object?>
            {
                ["date"] = regularization.Date.ToString("yyyy-MM-dd"),
                ["regularization_type"] = regularization.RegularizationType,
            };
            var wf = await _workflowRuntime.CreateInstanceOnSubmitAsync(
                WorkflowEntityType.Attendance, regularization.Id, employee.Id, requestData, cancellationToken);
            if (wf.InstanceCreated)
            {
                regularization.WorkflowInstanceId = wf.InstanceId;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        // FR-4: notify the employee's line manager that a regularization request awaits approval (US-NTF-006
        // Phase 6). The service never throws, so a delivery failure cannot break the committed request.
        if (_notifications is not null)
        {
            await _notifications.NotifyRegularizationRequestedAsync(
                employee.Id, regularization.Date, regularization.Reason, cancellationToken);
        }

        _logger.LogInformation(
            "Employee {EmployeeId} submitted regularization {RegId} ({Type}) for {Date} (tenant {TenantId}).",
            employee.Id, regularization.Id, type, regularization.Date, _tenantContext.TenantId);

        return Result<RegularizationDto>.Success(MapToDto(regularization));
    }

    public async Task<Result<IReadOnlyList<RegularizationDto>>> GetMyRegularizationsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<RegularizationDto>>.Failure("Tenant context is not resolved.", 400);

        var employee = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);
        if (employee is null)
            return Result<IReadOnlyList<RegularizationDto>>.Failure(
                "No employee record is linked to the current user.", 403);

        var rows = await _dbContext.AttendanceRegularizations
            .AsNoTracking()
            .Where(r => r.EmployeeId == employee.Id)
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        IReadOnlyList<RegularizationDto> dtos = rows.Select(MapToDto).ToList();
        return Result<IReadOnlyList<RegularizationDto>>.Success(dtos);
    }

    // ── Audit (ISSUE-067/069/071) ──────────────────────────────────────
    private static readonly JsonSerializerOptions AuditJsonOptions = new() { WriteIndented = false };

    /// <summary>
    /// ISSUE-067/069/071: appends a queryable tenant audit row (with before/after JSON snapshots) to the
    /// shared audit_log table. Tenant/actor are stamped from context (the actor is the self-service
    /// employee for clock-in/out/regularization). Mirrors <c>LeaveTypeService.AddLeaveTypeAudit</c>; added
    /// to the change tracker and persisted by the caller's SaveChanges (same transaction as the write).
    /// </summary>
    private void AddAttendanceAudit(
        string action, string resourceType, Guid? resourceId, string? before, string? after, string? detail)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = action,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId?.ToString(),
            Before = before,
            After = after,
            Detail = detail,
            CreatedAt = UtcNow,
        });
    }

    private static string SnapshotLog(AttendanceLog log) => JsonSerializer.Serialize(new
    {
        log.EmployeeId,
        log.ClockIn,
        log.ClockOut,
        log.TotalWorkMinutes,
        log.OvertimeMinutes,
        log.Status,
        log.IsLate,
        log.LateMinutes,
        log.IsEarlyDeparture,
        log.EarlyDepartureMinutes,
        log.Source,
    }, AuditJsonOptions);

    private static string SnapshotRegularization(AttendanceRegularization r) => JsonSerializer.Serialize(new
    {
        r.EmployeeId,
        r.Date,
        r.RegularizationType,
        r.RequestedClockIn,
        r.RequestedClockOut,
        r.Reason,
        r.Status,
        r.AttendanceLogId,
    }, AuditJsonOptions);

    private static RegularizationDto MapToDto(AttendanceRegularization r) => new()
    {
        Id = r.Id,
        EmployeeId = r.EmployeeId,
        AttendanceLogId = r.AttendanceLogId,
        Date = r.Date,
        RegularizationType = r.RegularizationType,
        RequestedClockIn = r.RequestedClockIn,
        RequestedClockOut = r.RequestedClockOut,
        Reason = r.Reason,
        Status = r.Status,
        CreatedAt = r.CreatedAt,
    };

    /// <summary>
    /// US-ATT-009 AC-4: true when <paramref name="date"/> falls within an ACTIVE locked attendance
    /// period for the tenant (the canonical AttendancePeriodLock). Tenant-scoped via the global filter.
    /// </summary>
    private Task<bool> IsDateLockedAsync(DateOnly date, CancellationToken cancellationToken)
        => _dbContext.AttendancePeriodLocks
            .AnyAsync(p => p.IsLocked && p.PeriodStart <= date && p.PeriodEnd >= date, cancellationToken);

    /// <summary>
    /// Returns the tenant's attendance settings, creating a default (all enforcement off) row if none
    /// exists. The TenantInterceptor stamps TenantId on the new row; we set it explicitly for clarity.
    /// </summary>
    /// <summary>
    /// US-ATT-011 AC-3 (CAL-4): the EFFECTIVE policy for this employee — their Location's override, else the
    /// tenant default, else a lazily-created tenant-default row.
    ///
    /// <para>⚠ This used to be an unpredicated <c>FirstOrDefaultAsync()</c>, safe only because a unique index
    /// on TenantId alone guaranteed exactly one settings row per tenant. Location override rows break that
    /// invariant, so an unpredicated read would now return an ARBITRARY row and could apply one branch's
    /// geofence/OT policy to the whole tenant. Resolve per employee; never read unpredicated.</para>
    /// </summary>
    private Task<AttendanceSettings> GetOrCreateSettingsAsync(
        Employee employee, CancellationToken cancellationToken)
        => AttendancePolicyResolver.ResolveForLocationAsync(
            _dbContext, employee.LocationId, cancellationToken);

    /// <summary>
    /// US-ATT-008 BR-3/BR-6: resolves the shift signals used for inline late/early detection for an
    /// employee on a date. Reuses <see cref="IShiftService.ResolveForEmployeeAsync"/> (US-ATT-005) for
    /// assignment → rotation → tenant-default fallback. Returns null (→ NO late/early tracking) when:
    ///   - the shift is FLEXIBLE (no fixed start/end — BR-6/§10), or
    ///   - no shift resolves at all (no assignment and no tenant default).
    /// Grace falls back to the tenant <see cref="AttendanceSettings.GracePeriodMinutes"/> when the shift
    /// carries none (BR-3). Minimum minutes come from the shift's minimum hours (BR-2; 0 = no minimum gate).
    /// </summary>
    private async Task<ShiftLateEarlySignals?> ResolveShiftSignalsAsync(
        Guid employeeId, DateOnly date, AttendanceSettings settings, CancellationToken cancellationToken)
    {
        var resolved = await _shiftService.ResolveForEmployeeAsync(employeeId, date, cancellationToken);
        if (resolved.IsFailure || resolved.Value is null)
            return null;

        var shift = resolved.Value;

        // BR-6 / §10: FLEXIBLE shifts (no fixed start/end) are exempt from late/early tracking.
        if (string.Equals(shift.Type, ShiftType.Flexible, StringComparison.OrdinalIgnoreCase))
            return null;

        var start = ParseTime(shift.StartTime);
        var end = ParseTime(shift.EndTime);
        if (start is null && end is null)
            return null;   // nothing to evaluate against.

        // BR-3: shift grace, else tenant default, else 0.
        var grace = shift.GracePeriodMinutes > 0 ? shift.GracePeriodMinutes : settings.GracePeriodMinutes;
        var minimumMinutes = shift.MinimumHours is { } mh ? (int)Math.Round(mh * 60m) : 0;

        // US-ATT-008 BR-8: an approved HALF-DAY leave means only half the shift is expected. Evaluate
        // late/early against the WORKED half only — halve the minimum-hours gate, and move the expected
        // boundary to the shift midpoint for the on-leave half so that half is never flagged late/short:
        //   AM leave → employee works the PM half → expected START moves to the midpoint.
        //   PM leave → employee works the AM half → expected END moves to the midpoint.
        // Full-day-leave and no-leave paths are unchanged (no half-day row → this block is skipped).
        var halfDaySession = await ResolveHalfDayLeaveSessionAsync(employeeId, date, cancellationToken);
        if (halfDaySession is not null)
        {
            minimumMinutes /= 2;   // half a day expected (0 stays 0 = no minimum gate).
            if (start is { } s && end is { } e && e > s)
            {
                var mid = TimeOnly.FromTimeSpan(s.ToTimeSpan() + (e.ToTimeSpan() - s.ToTimeSpan()) / 2);
                if (string.Equals(halfDaySession, "AM", StringComparison.OrdinalIgnoreCase))
                    start = mid;
                else if (string.Equals(halfDaySession, "PM", StringComparison.OrdinalIgnoreCase))
                    end = mid;
            }
        }

        return new ShiftLateEarlySignals(start, end, Math.Max(0, grace), minimumMinutes);
    }

    /// <summary>
    /// US-ATT-008 BR-8: the session ("AM"/"PM") of an APPROVED half-day leave covering <paramref name="date"/>
    /// for the employee, or null when there is none. Tenant-scoped via the global query filter. A half-day
    /// request has StartDate == EndDate; the range predicate covers that single day.
    /// </summary>
    private async Task<string?> ResolveHalfDayLeaveSessionAsync(
        Guid employeeId, DateOnly date, CancellationToken cancellationToken)
        => await _dbContext.LeaveRequests
            .AsNoTracking()
            .Where(lr => lr.EmployeeId == employeeId
                && lr.Status == LeaveRequestStatus.Approved
                && lr.IsHalfDay
                && lr.StartDate <= date && lr.EndDate >= date)
            .OrderByDescending(lr => lr.RequestedAt)
            .Select(lr => lr.HalfDaySession)
            .FirstOrDefaultAsync(cancellationToken);

    private readonly record struct ShiftLateEarlySignals(
        TimeOnly? Start, TimeOnly? End, int Grace, int MinimumMinutes);

    private static TimeOnly? ParseTime(string? hhmm)
        => TimeOnly.TryParse(hhmm, out var t) ? t : null;

    /// <summary>
    /// ISSUE-065: resolves the tenant's configured IANA time zone (locale.time_zone, validated on write —
    /// BUG-005) into a <see cref="TimeZoneInfo"/> so UTC punch instants can be projected to tenant-local
    /// day / time-of-day for day-boundary and late/early derivation. Resolution + the UTC fallback (for a
    /// missing or unrecognized zone — never throws) live in <see cref="TenantClock.ResolveTimeZone"/>.
    /// </summary>
    private async Task<TimeZoneInfo> ResolveTenantTimeZoneAsync(CancellationToken cancellationToken)
    {
        var tzId = await _dbContext.Tenants
            .AsNoTracking()
            .Where(t => t.Id == _tenantContext.TenantId)
            .Select(t => t.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);

        return TenantClock.ResolveTimeZone(tzId, _logger);
    }

    /// <summary>
    /// US-ATT-008 FR-7 guard (b): true when the employee already has ANOTHER late attendance log whose
    /// tenant-local date equals <paramref name="localDate"/>, besides <paramref name="excludeLogId"/> (the punch
    /// just committed). Loads late logs in a UTC window wide enough to cover any zone offset (±1 day) and compares
    /// on the LOCAL date. Used to fire the chronic escalation only on the FIRST late punch of the crossing day.
    /// </summary>
    private async Task<bool> HasOtherLateLogOnLocalDateAsync(
        Guid employeeId, Guid excludeLogId, DateOnly localDate, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var windowStart = localDate.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var windowEnd = localDate.AddDays(2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var candidates = await _dbContext.AttendanceLogs.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.IsLate
                && a.Id != excludeLogId
                && a.ClockIn >= windowStart && a.ClockIn < windowEnd)
            .Select(a => a.ClockIn)
            .ToListAsync(cancellationToken);

        return candidates.Any(clockIn => TenantClock.LocalDateOf(clockIn, zone) == localDate);
    }

    private async Task TryRecordIdempotencyAsync(
        string key, AttendanceLogDto dto, CancellationToken cancellationToken)
    {
        try
        {
            _dbContext.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                IdempotencyKey = key,
                OperationName = OperationName,
                ResponseJson = JsonSerializer.Serialize(dto),
                ResponseStatusCode = 201,
                CreatedAt = UtcNow,
                ExpiresAt = UtcNow.Add(IdempotencyWindow),
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent request with the same key already wrote the record (unique index).
            // The clock-in itself succeeded; the duplicate idempotency row is harmless to drop.
            _logger.LogDebug("Idempotency key {Key} already recorded for {Operation}.", key, OperationName);
        }
    }

    private static AttendanceLogDto MapToDto(AttendanceLog log) => new()
    {
        Id = log.Id,
        EmployeeId = log.EmployeeId,
        ClockIn = log.ClockIn,
        ClockOut = log.ClockOut,
        ClockInLatitude = log.ClockInLatitude,
        ClockInLongitude = log.ClockInLongitude,
        Source = log.Source,
        IsLate = log.IsLate,                 // US-ATT-008: FE "Late" badge.
        LateMinutes = log.LateMinutes,
        CreatedAt = log.CreatedAt,
    };

    /// <summary>
    /// Maps a CLOSED attendance log to the clock-out summary DTO (US-ATT-002). Assumes
    /// <see cref="AttendanceLog.ClockOut"/> and the computed fields are set; coalesces defensively.
    /// </summary>
    private static ClockOutResultDto MapToResult(AttendanceLog log) => new()
    {
        Id = log.Id,
        EmployeeId = log.EmployeeId,
        ClockIn = log.ClockIn,
        ClockOut = log.ClockOut!.Value,
        TotalWorkMinutes = log.TotalWorkMinutes ?? 0,
        OvertimeMinutes = log.OvertimeMinutes ?? 0,
        Status = log.Status ?? "COMPLETE",
        IsEarlyDeparture = log.IsEarlyDeparture,   // US-ATT-008: FE "Early" badge.
        EarlyDepartureMinutes = log.EarlyDepartureMinutes,
    };

    /// <summary>
    /// Great-circle distance in metres between two lat/long points (FR-3 geo-fence check).
    /// </summary>
    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusM = 6_371_000d;
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2))
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusM * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;

    /// <summary>
    /// True when a <see cref="DbUpdateException"/> was caused by the partial unique index enforcing
    /// "at most one open record per employee" (Postgres 23505 / ix_attendance_log_open_unique). Used
    /// to translate a racing concurrent clock-in into a clean 409 instead of an HTTP 500 (BUG-047).
    /// </summary>
    private static bool IsOpenClockInViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            && pg.ConstraintName == "ix_attendance_log_open_unique";
}
