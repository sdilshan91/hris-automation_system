using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire recurring job that checks for documents approaching expiry (US-CHR-008 FR-8, BR-4, AC-5).
/// Runs daily at 09:00. Scans all tenants for documents expiring within 30/7/1 days.
///
/// DEFERRED: Actual notification dispatch is not implemented because the Notification module
/// does not exist yet. This job logs and records intent only.
/// TODO(notification-module): Wire INotificationService.SendDocumentExpiryWarningAsync() when available.
/// </summary>
public sealed class DocumentExpiryNotificationJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DocumentExpiryNotificationJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunAsync()
    {
        Log.Information("DocumentExpiryNotificationJob started.");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var today = DateTime.UtcNow.Date;
            var thresholds = new[] { 30, 7, 1 };

            foreach (var days in thresholds)
            {
                var targetDate = today.AddDays(days);

                // Query across all tenants (job runs at system level).
                // IgnoreQueryFilters() bypasses both tenant and soft-delete filters;
                // we explicitly filter IsDeleted = false.
                var expiringDocs = await dbContext.EmployeeDocuments
                    .IgnoreQueryFilters()
                    .Where(d => !d.IsDeleted
                        && d.ExpiryDate.HasValue
                        && d.ExpiryDate.Value == targetDate)
                    .Select(d => new
                    {
                        d.Id,
                        d.TenantId,
                        d.EmployeeId,
                        d.FileName,
                        d.Category,
                        d.ExpiryDate,
                    })
                    .ToListAsync();

                if (expiringDocs.Count > 0)
                {
                    Log.Information(
                        "DocumentExpiryNotificationJob: {Count} document(s) expiring in {Days} day(s) (target date: {TargetDate}).",
                        expiringDocs.Count, days, targetDate);

                    foreach (var doc in expiringDocs)
                    {
                        // TODO(notification-module): Replace this log with actual notification dispatch:
                        //   await notificationService.SendDocumentExpiryWarningAsync(
                        //       doc.TenantId, doc.EmployeeId, doc.Id, doc.FileName, doc.ExpiryDate, days);
                        Log.Information(
                            "DocumentExpiryNotificationJob: NOTIFICATION INTENT - " +
                            "DocumentId={DocumentId}, FileName={FileName}, Category={Category}, " +
                            "ExpiryDate={ExpiryDate}, DaysUntilExpiry={Days}, " +
                            "TenantId={TenantId}, EmployeeId={EmployeeId}",
                            doc.Id, doc.FileName, doc.Category,
                            doc.ExpiryDate, days,
                            doc.TenantId, doc.EmployeeId);
                    }
                }
            }

            Log.Information("DocumentExpiryNotificationJob completed.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DocumentExpiryNotificationJob failed.");
            throw; // Let Hangfire handle retry
        }
    }
}
