using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire recurring job that checks for documents approaching expiry (US-CHR-008 FR-8, BR-4, AC-5).
/// Runs daily at 09:00. Scans all tenants for documents expiring within 30/7/1 days.
///
/// US-NTF-006 Phase 7: for each expiring document it dispatches a real warning (in-app + email) to the
/// document-owner employee via <see cref="ICoreHrNotificationService"/>. This is a cross-tenant scan under the
/// system context, so each document's own <c>TenantId</c> is passed EXPLICITLY into the notify call.
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

            // RLS increment 2c: this is a cross-tenant scan (IgnoreQueryFilters spans every tenant). Establish the
            // system/admin context so the ambient routes to the privileged (BYPASSRLS) connection under RLS and
            // caches under the sys: prefix (closing the increment-1 shared-none: gap).
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetSystemContext();

            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notifications = scope.ServiceProvider.GetRequiredService<ICoreHrNotificationService>();

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
                        // US-NTF-006 Phase 7: dispatch the real expiry warning to the document-owner employee. Cross-
                        // tenant scan → pass the document's own TenantId explicitly. The notify call never throws.
                        await notifications.NotifyDocumentExpiryAsync(
                            doc.TenantId, doc.EmployeeId, doc.FileName, doc.Category.ToString(),
                            DateOnly.FromDateTime(doc.ExpiryDate!.Value), days);
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
