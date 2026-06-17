using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-RPT-004 (BR-3): cross-tenant report-export retention cleanup. Finds every Completed export whose 7-day
/// <c>ExpiresAt</c> has passed, marks it <c>Expired</c>, deletes its stored file via the local storage path, and
/// writes a "HrReport.ExportExpired" audit row. Runs in the system/admin context (IgnoreQueryFilters with explicit
/// per-row tenant scoping). Provider-agnostic — works on InMemory, so the core logic is directly testable without
/// Hangfire or Postgres. Mirrors the US-ADM-010 ExportCleanupService.
/// </summary>
public sealed class HrReportExportCleanupService : IHrReportExportCleanupService
{
    private readonly AppDbContext _db;
    private readonly ILogger<HrReportExportCleanupService> _logger;

    public HrReportExportCleanupService(AppDbContext db, ILogger<HrReportExportCleanupService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result<int>> ExpireOverdueExportsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var overdue = await _db.HrReportExports
            .IgnoreQueryFilters()
            .Where(e => !e.IsDeleted
                && e.Status == HrReportExportStatus.Completed
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
                        "HrReportExportCleanup: failed to delete file for export {ExportId} (tenant {TenantId}).",
                        export.Id, export.TenantId);
                }
            }

            export.Status = HrReportExportStatus.Expired;
            export.UpdatedAt = now;

            _db.AuditLogs.Add(new AuditLog
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = export.TenantId,
                EventType = "HrReport.ExportExpired",
                Action = "HrReport.ExportExpired",
                ResourceType = "HrReportExport",
                ResourceId = export.Id.ToString(),
                CreatedAt = now,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("HrReportExportCleanup: expired {Count} overdue report export(s).", overdue.Count);
        return Result<int>.Success(overdue.Count);
    }
}
