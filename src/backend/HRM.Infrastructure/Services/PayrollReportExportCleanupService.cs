using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// ISSUE-178 PR2: cross-tenant payroll-report-export retention cleanup. Finds every Completed export whose 7-day
/// <c>ExpiresAt</c> has passed, marks it <c>Expired</c>, deletes its stored file, and writes a
/// "PayrollReport.ExportExpired" audit row. Runs in the system/admin context (IgnoreQueryFilters with explicit
/// per-row tenant scoping). Provider-agnostic — works on InMemory, so the core logic is directly testable without
/// Hangfire or Postgres. A 1:1 clone of <see cref="HrReportExportCleanupService"/>.
/// </summary>
public sealed class PayrollReportExportCleanupService : IPayrollReportExportCleanupService
{
    private readonly AppDbContext _db;
    private readonly ILogger<PayrollReportExportCleanupService> _logger;

    public PayrollReportExportCleanupService(AppDbContext db, ILogger<PayrollReportExportCleanupService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result<int>> ExpireOverdueExportsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var overdue = await _db.PayrollReportExports
            .IgnoreQueryFilters()
            .Where(e => !e.IsDeleted
                && e.Status == PayrollReportExportStatus.Completed
                && e.ExpiresAt != null && e.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        if (overdue.Count == 0)
            return Result<int>.Success(0);

        foreach (var export in overdue)
        {
            if (!string.IsNullOrWhiteSpace(export.FilePath))
            {
                try
                {
                    if (File.Exists(export.FilePath))
                        File.Delete(export.FilePath);
                }
                catch (Exception ex)
                {
                    // Best-effort delete: still mark Expired so it is no longer downloadable.
                    _logger.LogWarning(ex,
                        "PayrollReportExportCleanup: failed to delete file for export {ExportId} (tenant {TenantId}).",
                        export.Id, export.TenantId);
                }
            }

            export.Status = PayrollReportExportStatus.Expired;
            export.UpdatedAt = now;

            _db.AuditLogs.Add(new AuditLog
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = export.TenantId,
                EventType = "PayrollReport.ExportExpired",
                Action = "PayrollReport.ExportExpired",
                ResourceType = "PayrollReportExport",
                ResourceId = export.Id.ToString(),
                CreatedAt = now,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("PayrollReportExportCleanup: expired {Count} overdue report export(s).", overdue.Count);
        return Result<int>.Success(overdue.Count);
    }
}
