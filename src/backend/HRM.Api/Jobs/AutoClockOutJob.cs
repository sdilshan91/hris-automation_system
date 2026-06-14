using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
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

    // Page size for the per-tenant open-record scan (mirrors ProcessAbsenteeismJob batching).
    private const int PageSize = 500;

    public AutoClockOutJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting AutoClockOutJob");

        var now = DateTime.UtcNow;
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

        // Restore the tenant context for this scope so global query filters apply (tenant isolation).
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        if (tenantContext is Infrastructure.Services.TenantContext mutableContext)
        {
            mutableContext.SetTenant(tenantId, $"tenant-{tenantId}", TenantStatus.Active);
        }

        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // One settings row per tenant; fall back to defaults when none exists (read-only — don't
        // create a row as a side effect).
        var settings = await dbContext.AttendanceSettings.FirstOrDefaultAsync()
                       ?? new AttendanceSettings { TenantId = tenantId };

        int closed = 0;
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

            foreach (var log in openLogs)
            {
                // System clock-out at the end of the clock-in's own UTC day (23:59:59).
                var clockInDayStart = log.ClockIn.Date;
                var systemClockOut = DateTime.SpecifyKind(
                    clockInDayStart.AddDays(1).AddSeconds(-1), DateTimeKind.Utc);

                var calc = AttendanceCalculator.Calculate(
                    log.ClockIn, systemClockOut, settings, isSystemClosed: true);

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

        if (closed > 0)
            Log.Information(
                "AutoClockOutJob: Auto-closed {Count} open attendance record(s) for tenant {TenantId}",
                closed, tenantId);
    }
}
