using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// ISSUE-178 PR2: export of the payroll reports (US-PAY-009 / US-RPT-003) to CSV / Excel / PDF, with a sync
/// fast-path for small reports and an async Hangfire path for large ones. A deliberate 1:1 clone of
/// <see cref="HrReportExportService"/>, adapted for payroll.
///
/// <para>Initiation (<see cref="InitiateAsync"/>): validates the report type + format, enforces the per-user
/// concurrency limit (3 in-progress), regenerates the report to learn the row count, then routes — small reports
/// (&lt; 1000 rows) are rendered + stored + Completed INLINE; large reports (&ge; 1000 rows) are Queued and
/// generation is enqueued on the optional <see cref="IPayrollReportExportJobScheduler"/> (Hangfire in HRM.Api;
/// absent in tests, where the job/test calls <see cref="GenerateAsync"/> directly). Every export is audited.</para>
///
/// <para>Tenant isolation (AC-5): all reads/writes run under the EF global query filter
/// (TenantId == ITenantContext.TenantId). The download path additionally checks the OWNER (RequestedByUserId), so
/// a cross-tenant download is naturally a 404 (filtered out) and a cross-user one likewise 404.</para>
///
/// <para>REUSE: the actual bytes are produced by the existing, shipped
/// <see cref="IPayrollReportService.ExportReportAsync"/> — NOT a raw <c>PayrollReportRenderer.Render</c> — so the
/// BankAdvice FULL-account-number unmasking (BR-2) stays consistent with the synchronous GET export endpoint.
/// <see cref="IPayrollReportService.GenerateReportAsync"/> is used ONLY to learn the row count for the sync/async
/// routing (the masked-vs-unmasked distinction never changes the row COUNT). Storage reuses the existing
/// <see cref="IReportExportStorage"/> seam; async notify reuses the <see cref="INotificationService"/> seam.</para>
/// </summary>
public sealed class PayrollReportExportService : IPayrollReportExportService
{
    /// <summary>Reports with fewer rows than this render synchronously; the rest go async (same value as US-RPT-004).</summary>
    public const int AsyncRowThreshold = 1000;

    /// <summary>A single user may have at most this many Queued/Processing exports at once.</summary>
    public const int MaxConcurrentPerUser = 3;

    /// <summary>Completed export files are retained for 7 days, then purged.</summary>
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPayrollReportService _reportService;
    private readonly IReportExportStorage _storage;
    private readonly ILogger<PayrollReportExportService> _logger;
    private readonly INotificationService? _notifications;
    private readonly IPayrollReportExportJobScheduler? _scheduler;

    public PayrollReportExportService(
        AppDbContext db,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IPayrollReportService reportService,
        IReportExportStorage storage,
        ILogger<PayrollReportExportService> logger,
        INotificationService? notifications = null,
        IPayrollReportExportJobScheduler? scheduler = null)
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

    // ── Initiation (sync fast-path vs async enqueue) ─────────────────────────

    public async Task<Result<PayrollReportExportInitiatedDto>> InitiateAsync(
        string reportType,
        PayrollReportQueryParams filters,
        string format,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollReportExportInitiatedDto>.Failure("No tenant context resolved.", 400);

        if (!Enum.TryParse<PayrollReportType>(reportType, ignoreCase: true, out var type))
            return Result<PayrollReportExportInitiatedDto>.Failure(
                $"Unknown report type '{reportType}'.", 400, "unknown_report_type");

        if (!TryParseFormat(format, out var appFormat, out var domainFormat))
            return Result<PayrollReportExportInitiatedDto>.Failure(
                $"Unsupported export format '{format}'. Use csv, xlsx, or pdf.", 400, "unsupported_format");

        var userId = _currentUser.IsAuthenticated ? _currentUser.UserId : Guid.Empty;

        // Per-user concurrency limit. 4th concurrent request (3 in-progress) → 429.
        var inProgress = await _db.PayrollReportExports
            .Where(e => e.RequestedByUserId == userId
                && (e.Status == PayrollReportExportStatus.Queued || e.Status == PayrollReportExportStatus.Processing))
            .CountAsync(cancellationToken);
        if (inProgress >= MaxConcurrentPerUser)
            return Result<PayrollReportExportInitiatedDto>.Failure(
                "You already have the maximum number of exports in progress. Please wait for one to finish.",
                429, "export_limit_reached");

        // Regenerate the report to learn the row count (sync/async routing). The BankAdvice masked-vs-unmasked
        // distinction never changes the ROW COUNT, so this masked read is a correct count source.
        var reportResult = await _reportService.GenerateReportAsync(type, filters, cancellationToken);
        if (reportResult.IsFailure)
            return Result<PayrollReportExportInitiatedDto>.Failure(
                reportResult.Error!, reportResult.StatusCode ?? 400, reportResult.ErrorCode);

        var rowCount = reportResult.Value!.Rows.Count;
        var now = DateTime.UtcNow;

        var export = new PayrollReportExport
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            RequestedByUserId = userId,
            ReportType = type.ToString(),
            Format = domainFormat,
            FiltersJson = JsonSerializer.Serialize(filters, JsonOptions),
            RowCount = rowCount,
            Status = PayrollReportExportStatus.Queued,
            RequestedAt = now,
            CreatedAt = now,
            CreatedBy = userId == Guid.Empty ? "system" : userId.ToString(),
        };

        if (rowCount < AsyncRowThreshold)
        {
            // ── SYNC path: render inline (via the shipped export path → BankAdvice unmasked), store, complete. ──
            var exportResult = await _reportService.ExportReportAsync(type, appFormat, filters, cancellationToken);
            if (exportResult.IsFailure)
                return Result<PayrollReportExportInitiatedDto>.Failure(
                    exportResult.Error!, exportResult.StatusCode ?? 400, exportResult.ErrorCode);

            var file = exportResult.Value!;
            var location = await _storage.SaveAsync(
                _tenantContext.TenantId, export.Id, file.FileName, file.ContentType, file.FileContent, cancellationToken);

            export.Status = PayrollReportExportStatus.Completed;
            export.CompletedAt = now;
            export.ExpiresAt = now.Add(RetentionWindow);
            export.FilePath = location;
            export.FileSizeBytes = file.FileContent.LongLength;

            _db.PayrollReportExports.Add(export);
            AddExportAudit(export, now);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "PayrollReportExport {ExportId} ({ReportType}/{Format}) completed synchronously: {Rows} rows, {Bytes} bytes.",
                export.Id, export.ReportType, export.Format, rowCount, file.FileContent.Length);

            return Result<PayrollReportExportInitiatedDto>.Success(new PayrollReportExportInitiatedDto
            {
                ExportId = export.Id,
                Status = PayrollReportExportStatus.Completed.ToString(),
                RowCount = rowCount,
                Format = format.ToLowerInvariant(),
            });
        }

        // ── ASYNC path: queue + enqueue the Hangfire job. ──
        _db.PayrollReportExports.Add(export);
        AddExportAudit(export, now);
        await _db.SaveChangesAsync(cancellationToken);

        _scheduler?.EnqueueGeneration(_tenantContext.TenantId, export.Id);

        _logger.LogInformation(
            "PayrollReportExport {ExportId} ({ReportType}/{Format}) queued for async generation: {Rows} rows.",
            export.Id, export.ReportType, export.Format, rowCount);

        return Result<PayrollReportExportInitiatedDto>.Success(new PayrollReportExportInitiatedDto
        {
            ExportId = export.Id,
            Status = PayrollReportExportStatus.Queued.ToString(),
            RowCount = rowCount,
            Format = format.ToLowerInvariant(),
        });
    }

    // ── Generation (async body) ──────────────────────────────────────────────

    public async Task<Result> GenerateAsync(Guid exportId, CancellationToken cancellationToken = default)
    {
        var export = await _db.PayrollReportExports
            .FirstOrDefaultAsync(e => e.Id == exportId, cancellationToken);
        if (export is null)
            return Result.Failure("Export not found.", 404, "export_not_found");

        if (export.Status == PayrollReportExportStatus.Completed)
            return Result.Success(); // idempotent — already generated.

        export.Status = PayrollReportExportStatus.Processing;
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            if (!Enum.TryParse<PayrollReportType>(export.ReportType, ignoreCase: true, out var type))
                throw new InvalidOperationException($"Stored report type '{export.ReportType}' is no longer valid.");

            var appFormat = ToAppFormat(export.Format);
            var filters = JsonSerializer.Deserialize<PayrollReportQueryParams>(export.FiltersJson, JsonOptions)
                ?? new PayrollReportQueryParams();

            var exportResult = await _reportService.ExportReportAsync(type, appFormat, filters, cancellationToken);
            if (exportResult.IsFailure)
                throw new InvalidOperationException(exportResult.Error ?? "Report export failed.");

            var file = exportResult.Value!;
            var location = await _storage.SaveAsync(
                export.TenantId, export.Id, file.FileName, file.ContentType, file.FileContent, cancellationToken);

            var now = DateTime.UtcNow;
            export.Status = PayrollReportExportStatus.Completed;
            export.CompletedAt = now;
            export.ExpiresAt = now.Add(RetentionWindow);
            export.FilePath = location;
            export.FileSizeBytes = file.FileContent.LongLength;
            await _db.SaveChangesAsync(cancellationToken);

            // In-app SignalR notification on async-complete (reuses the US-RPT-004 "ReportExportReady" type).
            if (_notifications is not null && export.RequestedByUserId != Guid.Empty)
            {
                await _notifications.CreateAndDispatchAsync(
                    export.TenantId,
                    export.RequestedByUserId,
                    type: "ReportExportReady",
                    title: "Your payroll report export is ready",
                    message: $"Your {export.Format.ToString().ToUpperInvariant()} export of the {export.ReportType} payroll report is ready to download.",
                    resourceType: "PayrollReportExport",
                    resourceId: export.Id.ToString(),
                    cancellationToken);
            }

            _logger.LogInformation(
                "PayrollReportExport {ExportId} ({ReportType}/{Format}) completed asynchronously: {Rows} rows, {Bytes} bytes.",
                export.Id, export.ReportType, export.Format, export.RowCount, file.FileContent.Length);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayrollReportExport {ExportId} generation failed.", export.Id);

            export.Status = PayrollReportExportStatus.Failed;
            export.CompletedAt = DateTime.UtcNow;
            export.Error = ex.Message;
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Failure("Export generation failed.", 500, "export_failed");
        }
    }

    // ── History ──────────────────────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<PayrollReportExportListItemDto>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<PayrollReportExportListItemDto>>.Failure("No tenant context resolved.", 400);

        var userId = _currentUser.IsAuthenticated ? _currentUser.UserId : Guid.Empty;
        var now = DateTime.UtcNow;

        var rows = await _db.PayrollReportExports
            .AsNoTracking()
            .Where(e => e.RequestedByUserId == userId)
            .OrderByDescending(e => e.RequestedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        var dtos = rows.Select(e => new PayrollReportExportListItemDto
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
            DownloadReady = e.Status == PayrollReportExportStatus.Completed
                && e.ExpiresAt is { } exp && exp > now,
        }).ToList();

        return Result<IReadOnlyList<PayrollReportExportListItemDto>>.Success(dtos);
    }

    // ── Download (tenant + owner scoped) ─────────────────────────────────────

    public async Task<Result<PayrollReportExportDownloadDto?>> GetForDownloadAsync(
        Guid exportId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollReportExportDownloadDto?>.Failure("No tenant context resolved.", 400);

        // The global query filter scopes this to the caller's tenant — a cross-tenant id simply isn't found.
        var export = await _db.PayrollReportExports
            .FirstOrDefaultAsync(e => e.Id == exportId, cancellationToken);

        // Not found OR not owned by the caller → null (controller maps to 404). Returning null (rather than a
        // "found but not yours" 403) avoids leaking existence for a per-user, tenant-scoped resource.
        var userId = _currentUser.IsAuthenticated ? _currentUser.UserId : Guid.Empty;
        if (export is null || export.RequestedByUserId != userId)
            return Result<PayrollReportExportDownloadDto?>.Success(null);

        var now = DateTime.UtcNow;

        // Expired / purged → a distinct expired result (controller maps to 410).
        if (export.Status == PayrollReportExportStatus.Expired
            || (export.ExpiresAt is { } exp && exp <= now))
            return Result<PayrollReportExportDownloadDto?>.Success(PayrollReportExportDownloadDto.ExpiredResult());

        if (export.Status != PayrollReportExportStatus.Completed || string.IsNullOrWhiteSpace(export.FilePath))
            return Result<PayrollReportExportDownloadDto?>.Success(null);

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(export.FilePath, cancellationToken);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // File purged on disk but the row was not yet expired → treat as expired (410).
            return Result<PayrollReportExportDownloadDto?>.Success(PayrollReportExportDownloadDto.ExpiredResult());
        }

        // Audit the download.
        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = export.TenantId,
            UserId = userId == Guid.Empty ? null : userId,
            EventType = "PayrollReport.ExportDownloaded",
            Action = "PayrollReport.ExportDownloaded",
            ResourceType = "PayrollReportExport",
            ResourceId = export.Id.ToString(),
            Detail = $"reportType={export.ReportType}; format={export.Format}; rows={export.RowCount}",
            CreatedAt = now,
        });
        await _db.SaveChangesAsync(cancellationToken);

        var (fileName, contentType) = FileMetadata(export);
        return Result<PayrollReportExportDownloadDto?>.Success(
            PayrollReportExportDownloadDto.File(bytes, fileName, contentType));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Audit row written on EVERY export initiation (report type, filters, row count, format, actor).</summary>
    private void AddExportAudit(PayrollReportExport export, DateTime now)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = export.TenantId,
            UserId = export.RequestedByUserId == Guid.Empty ? null : export.RequestedByUserId,
            EventType = "PayrollReport.Export",
            Action = "PayrollReport.Export",
            ResourceType = "PayrollReportExport",
            ResourceId = export.Id.ToString(),
            Detail = $"reportType={export.ReportType}; format={export.Format}; rows={export.RowCount}; status={export.Status}",
            After = export.FiltersJson,
            CreatedAt = now,
        });
    }

    private static (string FileName, string ContentType) FileMetadata(PayrollReportExport export)
    {
        // Re-derive the download filename + content type from the stored path/format so the /download endpoint
        // serves a sensible attachment name without re-rendering.
        var fileName = string.IsNullOrWhiteSpace(export.FilePath)
            ? $"payroll-report-{export.ReportType}"
            : Path.GetFileName(export.FilePath);

        var contentType = export.Format switch
        {
            PayrollReportExportFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            PayrollReportExportFormat.Pdf => "application/pdf",
            _ => "text/csv",
        };
        return (fileName, contentType);
    }

    private static PayrollExportFormat ToAppFormat(PayrollReportExportFormat format) => format switch
    {
        PayrollReportExportFormat.Xlsx => PayrollExportFormat.Xlsx,
        PayrollReportExportFormat.Pdf => PayrollExportFormat.Pdf,
        _ => PayrollExportFormat.Csv,
    };

    private static bool TryParseFormat(string? value, out PayrollExportFormat appFormat, out PayrollReportExportFormat domainFormat)
    {
        appFormat = PayrollExportFormat.Csv;
        domainFormat = PayrollReportExportFormat.Csv;
        if (string.IsNullOrWhiteSpace(value)) return false;
        switch (value.Trim().ToLowerInvariant())
        {
            case "csv": appFormat = PayrollExportFormat.Csv; domainFormat = PayrollReportExportFormat.Csv; return true;
            case "xlsx": case "excel": appFormat = PayrollExportFormat.Xlsx; domainFormat = PayrollReportExportFormat.Xlsx; return true;
            case "pdf": appFormat = PayrollExportFormat.Pdf; domainFormat = PayrollReportExportFormat.Pdf; return true;
            default: return false;
        }
    }
}
