using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire safety-net job that auto-closes attendance records left open past the end of the day
/// (US-ATT-002 BR-5). For each active/trial tenant it restores the tenant context (so global query
/// filters apply), finds every <c>attendance_log</c> still open from a prior UTC day, sets a
/// system-generated <c>clock_out</c> at that day's 23:59:59 UTC boundary, computes work hours via the
/// shared <see cref="AttendanceCalculator"/>, and flags the record <c>Status = "ANOMALY"</c> for
/// regularization. Employees are expected to clock out manually; this is the backstop.
///
/// TENANT TIMEZONE: closes at UTC end-of-day. No tenant-timezone infra exists yet (same deferral as
/// US-ATT-001's "one open record" day-boundary note); revisit when tenant timezones land.
/// Idempotent and tenant-safe; mirrors <see cref="ProcessAbsenteeismJob"/>.
/// </summary>
public sealed class AutoClockOutJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;

    // Page size for the per-tenant open-record scan (mirrors ProcessAbsenteeismJob batching).
    private const int PageSize = 500;

    // TimeProvider is trailing-optional (defaults to TimeProvider.System, no DI registration needed —
    // mirrors AttendanceService / DF-43) so the UTC day-boundary can be driven deterministically in tests.
    public AutoClockOutJob(IServiceScopeFactory scopeFactory, TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting AutoClockOutJob");

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        // Only close records whose clock-in was before today's UTC start — i.e. left open overnight.
        var todayStartUtc = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);

        List<Guid> tenantIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await dbContext.Tenants
                .Where(t => !t.IsDeleted && (t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial))
                .Select(t => t.Id)
                .ToListAsync();
        }

        Log.Information("AutoClockOutJob: Processing {TenantCount} tenants", tenantIds.Count);

        foreach (var tenantId in tenantIds)
        {
            try
            {
                await ProcessTenantAsync(tenantId, todayStartUtc);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AutoClockOutJob: Failed to process tenant {TenantId}", tenantId);
                // Continue with other tenants; don't fail the whole job.
            }
        }

        Log.Information("Completed AutoClockOutJob");
    }

    private async Task ProcessTenantAsync(Guid tenantId, DateTime todayStartUtc)
    {
        using var scope = _scopeFactory.CreateScope();

        var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // DF-63: resolve the employee's assigned shift so DF-56's explicit per-shift work-minute knobs apply to
        // the auto-closed minutes too (they governed the manual clock-out path but not this backstop). Shares the
        // scope's tenant-scoped AppDbContext, so it runs inside the same tenant context/GUC the runner sets below.
        var shiftService = scope.ServiceProvider.GetRequiredService<IShiftService>();

        // RLS increment 2c: run the per-tenant body via the shared runner so it sets the tenant context (and,
        // gated on Rls:Enabled, the app.current_tenant GUC) — keeping the scan inside the RLS backstop.
        int closed = 0;
        await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}", async _ =>
        {
        // DF-22 / ISSUE-309 (US-ATT-011 AC-3): load the tenant's WHOLE attendance-policy set — the tenant
        // default (location_id IS NULL) AND every per-location override — in ONE read, then resolve per log
        // below so each branch's employees are auto-closed against THEIR policy, not one tenant-wide row.
        // Read-only: LoadAllAsync never creates a settings row as a side effect (this is the safety-net job).
        // A location with no override (or a tenant with no rows) resolves to null → the code-default fallback.
        var policyByLocation = await AttendancePolicyResolver.LoadAllAsync(dbContext, CancellationToken.None);

        // DF-63: memoize the resolved shift per (employee, clock-in date) so a page with repeat employees doesn't
        // re-run the assignment→rotation resolution. A null value is cached too (no shift → tenant/code default).
        var shiftByEmployeeDate = new Dictionary<(Guid EmployeeId, DateOnly Date), Shift?>();

        async Task<Shift?> ResolveShiftAsync(Guid employeeId, DateOnly date)
        {
            var key = (employeeId, date);
            if (shiftByEmployeeDate.TryGetValue(key, out var cached))
                return cached;

            Shift? shift = null;
            var resolved = await shiftService.ResolveForEmployeeAsync(employeeId, date, CancellationToken.None);
            if (!resolved.IsFailure && resolved.Value is { } dto)
                shift = await dbContext.Shifts.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == dto.Id);

            shiftByEmployeeDate[key] = shift;
            return shift;
        }

        while (true)
        {
            // Records still open from a prior UTC day. Ordered by clock-in for stable paging.
            var openLogs = await dbContext.AttendanceLogs
                .Where(a => a.ClockOut == null && a.ClockIn < todayStartUtc)
                .OrderBy(a => a.ClockIn)
                .Take(PageSize)
                .ToListAsync();
            if (openLogs.Count == 0)
                break;

            // DF-22 / ISSUE-309: batch-load each open log's owning employee LocationId for this page, so the
            // policy can be resolved per location without an N+1 and without reshaping the paged log query
            // (which would risk dropping orphan logs on an inner join). A missing employee → null location →
            // tenant default.
            var pageEmployeeIds = openLogs.Select(a => a.EmployeeId).Distinct().ToList();
            var locationByEmployee = await dbContext.Employees.AsNoTracking()
                .Where(e => pageEmployeeIds.Contains(e.Id))
                .Select(e => new { e.Id, e.LocationId })
                .ToDictionaryAsync(e => e.Id, e => e.LocationId);

            foreach (var log in openLogs)
            {
                // System clock-out at the end of the clock-in's own UTC day (23:59:59).
                var clockInDayStart = log.ClockIn.Date;
                var systemClockOut = DateTime.SpecifyKind(
                    clockInDayStart.AddDays(1).AddSeconds(-1), DateTimeKind.Utc);

                // DF-22 / ISSUE-309: the effective policy for THIS log's employee location (override → tenant
                // default → code default). For returns null for a tenant with no settings rows at all — keep
                // the original code-default fallback in that case.
                var locationId = locationByEmployee.GetValueOrDefault(log.EmployeeId);
                var settings = AttendancePolicyResolver.For(policyByLocation, locationId)
                               ?? new AttendanceSettings { TenantId = tenantId };

                // DF-63: resolve against the clock-in's UTC date — the same UTC-day basis this job already uses for
                // the close boundary above (tenant-timezone infra is still deferred; revisit both together when it
                // lands). A null shift / all-null-knob shift → byte-identical to the pre-DF-63 tenant-policy math.
                var shift = await ResolveShiftAsync(log.EmployeeId, DateOnly.FromDateTime(log.ClockIn));

                var calc = AttendanceCalculator.Calculate(
                    log.ClockIn, systemClockOut, settings, isSystemClosed: true, shift: shift);

                log.ClockOut = systemClockOut;
                log.ClockOutIp = null;
                log.TotalWorkMinutes = calc.TotalWorkMinutes;
                log.OvertimeMinutes = calc.OvertimeMinutes;
                log.Status = calc.Status;   // ANOMALY — flagged for regularization (BR-5).
            }

            await dbContext.SaveChangesAsync();
            closed += openLogs.Count;

            if (openLogs.Count < PageSize)
                break;
        }
        });

        if (closed > 0)
            Log.Information(
                "AutoClockOutJob: Auto-closed {Count} open attendance record(s) for tenant {TenantId}",
                closed, tenantId);
    }
}
