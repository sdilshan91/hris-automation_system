using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Reports.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-RPT-004: export of the generic HR / leave / attendance reports (US-RPT-001 / US-RPT-002) to CSV / Excel /
/// PDF. SEPARATE from payroll exports (US-PAY-009) and the US-ADM-010 tenant data export.
///
/// <para>Initiation (<see cref="InitiateAsync"/>): validates the report type + format, enforces the FR-10 per-user
/// concurrency limit (3 in-progress), regenerates the report to learn the row count, then routes — small reports
/// (&lt; 1000 rows, FR-5) are rendered + stored + Completed INLINE; large reports (&ge; 1000 rows) are Queued and
/// generation is enqueued on the optional <see cref="IHrReportExportJobScheduler"/> (Hangfire in HRM.Api; absent in
/// tests, where the job/test calls <see cref="GenerateAsync"/> directly). Every export is audited (FR-9).</para>
///
/// <para>Tenant isolation (AC-5 / NFR-7): all reads/writes run under the EF global query filter
/// (TenantId == ITenantContext.TenantId). The download path additionally checks the OWNER (RequestedByUserId), so a
/// cross-tenant download is naturally a 404 (filtered out) and a cross-user one a 403.</para>
///
/// <para>REUSE: rendering is the pure <see cref="HrReportRenderer"/> (mirrors US-PAY-009's PayrollReportRenderer);
/// storage is the existing <see cref="IReportExportStorage"/> seam; the report itself is regenerated via the
/// US-RPT-001 <see cref="IHrReportService"/>; async notify is the US-NTF-001 <see cref="INotificationService"/>
/// seam. No new export infra is introduced.</para>
///
/// <para>DEFERRED (documented): (1) charts-as-PNG embedded in the PDF — no server-side chart renderer exists, so the
/// PDF renders branding + title + filters + table + pagination + footer, NOT chart images (FR-4/AC-3 chart-image
/// part). (2) Cryptographic signed URLs + 15-min link expiry (FR-7/NFR-4) — storage is a local stub; the
/// authenticated, tenant + owner-scoped /download endpoint plus the 7-day retention (BR-3) satisfy the AC-5
/// isolation requirement. (3) BR-2 sensitive-masking is N/A: these reports carry no bank/national-id PII.</para>
/// </summary>
public sealed class HrReportExportService : IHrReportExportService
{
    /// <summary>FR-5: reports with fewer rows than this render synchronously; the rest go async.</summary>
    public const int AsyncRowThreshold = 1000;

    /// <summary>FR-10: a single user may have at most this many Queued/Processing exports at once.</summary>
    public const int MaxConcurrentPerUser = 3;

    /// <summary>BR-3: completed export files are retained for 7 days, then purged.</summary>
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IHrReportService _reportService;
    private readonly IReportExportStorage _storage;
    private readonly ILogger<HrReportExportService> _logger;
    private readonly INotificationService? _notifications;
    private readonly IHrReportExportJobScheduler? _scheduler;

    public HrReportExportService(
        AppDbContext db,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IHrReportService reportService,
        IReportExportStorage storage,
        ILogger<HrReportExportService> logger,
        INotificationService? notifications = null,
        IHrReportExportJobScheduler? scheduler = null)
    {
        _db = db;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _reportService = reportService;
        _storage = storage;
        _logger = logger;
        _notifications = notifications;
        _scheduler = scheduler;
    }

    // ── Initiation (FR-5 / FR-10) ────────────────────────────────────────────

    public async Task<Result<HrReportExportInitiatedDto>> InitiateAsync(
        string reportType,
        HrReportQueryParams filters,
        string format,
        bool includeCharts,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<HrReportExportInitiatedDto>.Failure("No tenant context resolved.", 400);

        if (!HrReportTypeKey.TryParse(reportType, out var type))
            return Result<HrReportExportInitiatedDto>.Failure(
                $"Unknown report type '{reportType}'.", 400, "unknown_report_type");

        if (!TryParseFormat(format, out var exportFormat))
            return Result<HrReportExportInitiatedDto>.Failure(
                $"Unsupported export format '{format}'. Use csv, xlsx, or pdf.", 400, "unsupported_format");

        var userId = _currentUser.IsAuthenticated ? _currentUser.UserId : Guid.Empty;

        // FR-10: per-user concurrency limit. 4th concurrent request (3 in-progress) → 429.
        var inProgress = await _db.HrReportExports
            .Where(e => e.RequestedByUserId == userId
                && (e.Status == HrReportExportStatus.Queued || e.Status == HrReportExportStatus.Processing))
            .CountAsync(cancellationToken);
        if (inProgress >= MaxConcurrentPerUser)
            return Result<HrReportExportInitiatedDto>.Failure(
                "You already have the maximum number of exports in progress. Please wait for one to finish.",
                429, "export_limit_reached");

        // Regenerate the report to learn the row count (FR-5 sync/async routing). Refresh=false so it can hit the
        // report cache; the export captures a point-in-time snapshot, so a slightly-stale cached read is fine.
        var reportResult = await _reportService.GenerateReportAsync(type, filters, refresh: false, cancellationToken);
        if (reportResult.IsFailure)
            return Result<HrReportExportInitiatedDto>.Failure(
                reportResult.Error!, reportResult.StatusCode ?? 400, reportResult.ErrorCode);

        var report = reportResult.Value!;
        var rowCount = report.Table.Rows.Count;
        var now = DateTime.UtcNow;

        var export = new HrReportExport
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            RequestedByUserId = userId,
            ReportType = HrReportTypeKey.ToKey(type),
            Format = exportFormat,
            FiltersJson = JsonSerializer.Serialize(filters, JsonOptions),
            RowCount = rowCount,
            Status = HrReportExportStatus.Queued,
            RequestedAt = now,
            CreatedAt = now,
            CreatedBy = userId == Guid.Empty ? "system" : userId.ToString(),
        };

        if (rowCount < AsyncRowThreshold)
        {
            // ── SYNC path (FR-5): render inline, store, complete. ──
            var (content, fileName, contentType) = HrReportRenderer.Render(
                exportFormat, report, await ResolveTenantNameAsync(_tenantContext.TenantId, cancellationToken));

            var location = await _storage.SaveAsync(
                _tenantContext.TenantId, export.Id, fileName, contentType, content, cancellationToken);

            export.Status = HrReportExportStatus.Completed;
            export.CompletedAt = now;
            export.ExpiresAt = now.Add(RetentionWindow);
            export.FilePath = location;
            export.FileSizeBytes = content.LongLength;

            _db.HrReportExports.Add(export);
            AddExportAudit(export, now);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "HrReportExport {ExportId} ({ReportType}/{Format}) completed synchronously: {Rows} rows, {Bytes} bytes.",
                export.Id, export.ReportType, export.Format, rowCount, content.Length);

            return Result<HrReportExportInitiatedDto>.Success(new HrReportExportInitiatedDto
            {
                ExportId = export.Id,
                Status = HrReportExportStatus.Completed.ToString(),
                RowCount = rowCount,
                Format = format.ToLowerInvariant(),
            });
        }

        // ── ASYNC path (FR-5): queue + enqueue the Hangfire job. ──
        _db.HrReportExports.Add(export);
        AddExportAudit(export, now);
        await _db.SaveChangesAsync(cancellationToken);

        _scheduler?.EnqueueGeneration(_tenantContext.TenantId, export.Id);

        _logger.LogInformation(
            "HrReportExport {ExportId} ({ReportType}/{Format}) queued for async generation: {Rows} rows.",
            export.Id, export.ReportType, export.Format, rowCount);

        return Result<HrReportExportInitiatedDto>.Success(new HrReportExportInitiatedDto
        {
            ExportId = export.Id,
            Status = HrReportExportStatus.Queued.ToString(),
            RowCount = rowCount,
            Format = format.ToLowerInvariant(),
        });
    }

    // ── Generation (FR-5 async / FR-8) ───────────────────────────────────────

    public async Task<Result> GenerateAsync(Guid exportId, CancellationToken cancellationToken = default)
    {
        var export = await _db.HrReportExports
            .FirstOrDefaultAsync(e => e.Id == exportId, cancellationToken);
        if (export is null)
            return Result.Failure("Export not found.", 404, "export_not_found");

        if (export.Status == HrReportExportStatus.Completed)
            return Result.Success(); // idempotent — already generated.

        export.Status = HrReportExportStatus.Processing;
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            if (!HrReportTypeKey.TryParse(export.ReportType, out var type))
                throw new InvalidOperationException($"Stored report type '{export.ReportType}' is no longer valid.");

            var filters = JsonSerializer.Deserialize<HrReportQueryParams>(export.FiltersJson, JsonOptions)
                ?? new HrReportQueryParams();

            var reportResult = await _reportService.GenerateReportAsync(type, filters, refresh: true, cancellationToken);
            if (reportResult.IsFailure)
                throw new InvalidOperationException(reportResult.Error ?? "Report generation failed.");

            var report = reportResult.Value!;
            var (content, fileName, contentType) = HrReportRenderer.Render(
                export.Format, report, await ResolveTenantNameAsync(export.TenantId, cancellationToken));

            var location = await _storage.SaveAsync(
                export.TenantId, export.Id, fileName, contentType, content, cancellationToken);

            var now = DateTime.UtcNow;
            export.Status = HrReportExportStatus.Completed;
            export.CompletedAt = now;
            export.ExpiresAt = now.Add(RetentionWindow);
            export.FilePath = location;
            export.FileSizeBytes = content.LongLength;
            export.RowCount = report.Table.Rows.Count;
            await _db.SaveChangesAsync(cancellationToken);

            // FR-8: in-app SignalR notification on async-complete.
            if (_notifications is not null && export.RequestedByUserId != Guid.Empty)
            {
                await _notifications.CreateAndDispatchAsync(
                    export.TenantId,
                    export.RequestedByUserId,
                    type: "ReportExportReady",
                    title: "Your report export is ready",
                    message: $"Your {export.Format.ToString().ToUpperInvariant()} export of the {report.Metadata.Title} report is ready to download.",
                    resourceType: "HrReportExport",
                    resourceId: export.Id.ToString(),
                    cancellationToken);
            }

            _logger.LogInformation(
                "HrReportExport {ExportId} ({ReportType}/{Format}) completed asynchronously: {Rows} rows, {Bytes} bytes.",
                export.Id, export.ReportType, export.Format, export.RowCount, content.Length);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HrReportExport {ExportId} generation failed.", export.Id);

            export.Status = HrReportExportStatus.Failed;
            export.CompletedAt = DateTime.UtcNow;
            export.Error = ex.Message;
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Failure("Export generation failed.", 500, "export_failed");
        }
    }

    // ── History (FR-9 / §8) ──────────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<HrReportExportListItemDto>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<HrReportExportListItemDto>>.Failure("No tenant context resolved.", 400);

        var userId = _currentUser.IsAuthenticated ? _currentUser.UserId : Guid.Empty;
        var now = DateTime.UtcNow;

        var rows = await _db.HrReportExports
            .AsNoTracking()
            .Where(e => e.RequestedByUserId == userId)
            .OrderByDescending(e => e.RequestedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        var dtos = rows.Select(e => new HrReportExportListItemDto
        {
            Id = e.Id,
            ReportType = e.ReportType,
            Format = e.Format.ToString().ToLowerInvariant(),
            Status = e.Status.ToString(),
            RequestedAt = e.RequestedAt,
            CompletedAt = e.CompletedAt,
            RowCount = e.RowCount,
            FileSizeBytes = e.FileSizeBytes,
            ExpiresAt = e.ExpiresAt,
            DownloadReady = e.Status == HrReportExportStatus.Completed
                && e.ExpiresAt is { } exp && exp > now,
        }).ToList();

        return Result<IReadOnlyList<HrReportExportListItemDto>>.Success(dtos);
    }

    // ── Download (AC-5) ──────────────────────────────────────────────────────

    public async Task<Result<HrReportExportDownloadDto?>> GetForDownloadAsync(
        Guid exportId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<HrReportExportDownloadDto?>.Failure("No tenant context resolved.", 400);

        // The global query filter scopes this to the caller's tenant — a cross-tenant id simply isn't found (AC-5).
        var export = await _db.HrReportExports
            .FirstOrDefaultAsync(e => e.Id == exportId, cancellationToken);

        // Not found OR not owned by the caller → null (controller maps to 404). Distinguishing a "found but
        // not yours" 403 would leak existence; returning null is the privacy-preserving choice for a per-user
        // resource that is also tenant-scoped.
        var userId = _currentUser.IsAuthenticated ? _currentUser.UserId : Guid.Empty;
        if (export is null || export.RequestedByUserId != userId)
            return Result<HrReportExportDownloadDto?>.Success(null);

        var now = DateTime.UtcNow;

        // Expired / purged → a distinct expired result (controller maps to 410).
        if (export.Status == HrReportExportStatus.Expired
            || (export.ExpiresAt is { } exp && exp <= now))
            return Result<HrReportExportDownloadDto?>.Success(HrReportExportDownloadDto.ExpiredResult());

        if (export.Status != HrReportExportStatus.Completed || string.IsNullOrWhiteSpace(export.FilePath))
            return Result<HrReportExportDownloadDto?>.Success(null);

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(export.FilePath, cancellationToken);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // File purged on disk but the row was not yet expired → treat as expired (410).
            return Result<HrReportExportDownloadDto?>.Success(HrReportExportDownloadDto.ExpiredResult());
        }

        // FR-9: audit the download.
        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = export.TenantId,
            UserId = userId == Guid.Empty ? null : userId,
            EventType = "HrReport.ExportDownloaded",
            Action = "HrReport.ExportDownloaded",
            ResourceType = "HrReportExport",
            ResourceId = export.Id.ToString(),
            Detail = $"reportType={export.ReportType}; format={export.Format}; rows={export.RowCount}",
            CreatedAt = now,
        });
        await _db.SaveChangesAsync(cancellationToken);

        var (_, fileName, contentType) = FileMetadata(export);
        return Result<HrReportExportDownloadDto?>.Success(
            HrReportExportDownloadDto.File(bytes, fileName, contentType));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>FR-9: audit row written on EVERY export initiation (report type, filters, row count, format, actor).</summary>
    private void AddExportAudit(HrReportExport export, DateTime now)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = export.TenantId,
            UserId = export.RequestedByUserId == Guid.Empty ? null : export.RequestedByUserId,
            EventType = "HrReport.Export",
            Action = "HrReport.Export",
            ResourceType = "HrReportExport",
            ResourceId = export.Id.ToString(),
            Detail = $"reportType={export.ReportType}; format={export.Format}; rows={export.RowCount}; status={export.Status}",
            After = export.FiltersJson,
            CreatedAt = now,
        });
    }

    private static (HrReportExportFormat Format, string FileName, string ContentType) FileMetadata(HrReportExport export)
    {
        // Re-derive the download filename + content type from the stored path/format so the /download endpoint
        // serves a sensible attachment name without re-rendering.
        var fileName = string.IsNullOrWhiteSpace(export.FilePath)
            ? $"hr-report-{export.ReportType}"
            : Path.GetFileName(export.FilePath);

        var contentType = export.Format switch
        {
            HrReportExportFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            HrReportExportFormat.Pdf => "application/pdf",
            _ => "text/csv",
        };
        return (export.Format, fileName, contentType);
    }

    private async Task<string?> ResolveTenantNameAsync(Guid tenantId, CancellationToken ct)
    {
        // Tenant is a platform table (no tenant query filter) — read it directly for the BR-5 PDF footer.
        return await _db.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct);
    }

    private static bool TryParseFormat(string? value, out HrReportExportFormat format)
    {
        format = HrReportExportFormat.Csv;
        if (string.IsNullOrWhiteSpace(value)) return false;
        switch (value.Trim().ToLowerInvariant())
        {
            case "csv": format = HrReportExportFormat.Csv; return true;
            case "xlsx": case "excel": format = HrReportExportFormat.Xlsx; return true;
            case "pdf": format = HrReportExportFormat.Pdf; return true;
            default: return false;
        }
    }
}
