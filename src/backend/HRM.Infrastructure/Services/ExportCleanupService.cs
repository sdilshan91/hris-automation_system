using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-ADM-010 (FR-7/NFR-4): cross-tenant export-retention cleanup. Finds every Completed export whose 72h
/// <c>ExpiresAt</c> has passed, marks it <c>Expired</c>, deletes its stored bundle through the tenant-isolated
/// <see cref="IFileStorage"/> seam, and writes a "DataExport.Expired" audit row. Runs in the system/admin context
/// (IgnoreQueryFilters with explicit per-row tenant scoping). Provider-agnostic — works on InMemory, so the core
/// logic is directly testable without Hangfire or Postgres.
/// </summary>
public sealed class ExportCleanupService : IExportCleanupService
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<ExportCleanupService> _logger;

    public ExportCleanupService(AppDbContext db, IFileStorage fileStorage, ILogger<ExportCleanupService> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<Result<int>> ExpireOverdueExportsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var overdue = await _db.ExportRequests
            .IgnoreQueryFilters()
            .Where(e => !e.IsDeleted
                && e.Status == ExportRequestStatus.Completed
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
                    await _fileStorage.DeleteAsync(export.TenantId, export.FilePath, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Best-effort delete: still mark Expired so it is no longer downloadable.
                    _logger.LogWarning(ex,
                        "ExportCleanup: failed to delete bundle for export {ExportId} (tenant {TenantId}).",
                        export.Id, export.TenantId);
                }
            }

            export.Status = ExportRequestStatus.Expired;
            export.UpdatedAt = now;

            _db.AuditLogs.Add(new AuditLog
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = export.TenantId,
                EventType = "DataExport.Expired",
                Action = "DataExport.Expired",
                ResourceType = "ExportRequest",
                ResourceId = export.Id.ToString(),
                CreatedAt = now,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("ExportCleanup: expired {Count} overdue export(s).", overdue.Count);
        return Result<int>.Success(overdue.Count);
    }
}
