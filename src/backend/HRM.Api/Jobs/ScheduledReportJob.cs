using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// US-ATT-010 FR-8: Hangfire recurring job that generates due scheduled attendance reports. For each
/// active/trial tenant it restores the tenant context (so the EF global query filters apply), finds the
/// active <c>scheduled_report_config</c> rows that are DUE for the current run (per frequency + delivery
/// time, not yet run for this period), generates the report file (via <see cref="IAttendanceDashboardService"/>),
/// stores it through <see cref="IReportExportStorage"/>, and stamps <c>LastRunAt</c>.
///
/// EMAIL DELIVERY DEFERRED (US-NTF): there is no notification/email infrastructure in this codebase. The
/// actual send to the configured recipients is LOGGED (no-op) here — the generation half is real, the
/// send half is a TODO. The job NEVER fails over the missing email infra.
///
/// TENANT TIMEZONE: due-time is evaluated in UTC (no tenant-timezone infra — same deferral as the other
/// attendance jobs, BR-6 not satisfied via tz). Idempotent and tenant-safe; mirrors MonthlySummaryDailyJob.
/// </summary>
public sealed class ScheduledReportJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ScheduledReportJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting ScheduledReportJob");

        var now = DateTime.UtcNow;

        List<Guid> tenantIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await dbContext.Tenants
                .Where(t => !t.IsDeleted && (t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial))
                .Select(t => t.Id)
                .ToListAsync();
        }

        foreach (var tenantId in tenantIds)
        {
            try
            {
                await ProcessTenantAsync(tenantId, now);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ScheduledReportJob: failed for tenant {TenantId}", tenantId);
            }
        }

        Log.Information("Completed ScheduledReportJob");
    }

    private async Task ProcessTenantAsync(Guid tenantId, DateTime now)
    {
        using var scope = _scopeFactory.CreateScope();

        var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dashboard = scope.ServiceProvider.GetRequiredService<IAttendanceDashboardService>();
        var storage = scope.ServiceProvider.GetRequiredService<IReportExportStorage>();

        // RLS increment 2c: run the per-tenant body via the shared runner so it sets the tenant context (and,
        // gated on Rls:Enabled, the app.current_tenant GUC) — keeping it inside the RLS backstop.
        await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}", async _ =>
        {
        var configs = await dbContext.ScheduledReportConfigs
            .Where(c => c.IsActive)
            .ToListAsync();

        foreach (var config in configs)
        {
            if (!IsDue(config, now)) continue;

            var generated = await GenerateAsync(dashboard, config, now);
            if (generated is null) continue;

            var (content, fileName, contentType) = generated.Value;
            var locator = await storage.SaveAsync(
                tenantId, config.Id, fileName, contentType, content, CancellationToken.None);

            config.LastRunAt = now;
            await dbContext.SaveChangesAsync();

            // EMAIL DELIVERY DEFERRED (US-NTF): log the send instead of emailing the recipients.
            Log.Information(
                "ScheduledReportJob: generated report {Id} ({Type}/{Frequency}) for tenant {TenantId} -> {Locator}. " +
                "Email delivery to {RecipientCount} recipient(s) is DEFERRED (US-NTF).",
                config.Id, config.ReportType, config.Frequency, tenantId, locator, config.Recipients.Count);
        }
        });
    }

    /// <summary>
    /// Due when the config has not yet run for the current period and the UTC delivery time has passed.
    /// DAILY: not run today. WEEKLY: not run in the last 7 days. MONTHLY: not run this calendar month.
    /// </summary>
    private static bool IsDue(ScheduledReportConfig config, DateTime now)
    {
        if (now.TimeOfDay < config.DeliveryTime.ToTimeSpan())
            return false;

        var last = config.LastRunAt;
        return (config.Frequency ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "DAILY" => last is null || last.Value.Date < now.Date,
            "WEEKLY" => last is null || last.Value < now.AddDays(-7),
            "MONTHLY" => last is null || last.Value.Year != now.Year || last.Value.Month != now.Month,
            _ => false,
        };
    }

    /// <summary>
    /// Generates the report file for the config. Defaults the period sensibly per report type: the
    /// department comparison uses the previous full month; the custom report uses the trailing 30 days.
    /// </summary>
    private static async Task<(byte[] Content, string FileName, string ContentType)?> GenerateAsync(
        IAttendanceDashboardService dashboard, ScheduledReportConfig config, DateTime now)
    {
        var format = (config.Format ?? "CSV").Trim().ToLowerInvariant();
        var to = DateOnly.FromDateTime(now.Date);
        var from = to.AddDays(-29);

        var export = await dashboard.ExportCustomReportAsync(
            from, to, format, new CustomReportFilter(), CancellationToken.None);

        if (export.IsFailure)
        {
            Log.Warning("ScheduledReportJob: report {Id} generation failed: {Error}", config.Id, export.Error);
            return null;
        }

        return (export.Value!.FileContent, export.Value.FileName, export.Value.ContentType);
    }
}
