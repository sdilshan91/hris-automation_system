using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.AuditLog;
using HRM.Application.Features.AuditLog.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-ADM-008: Tenant-Admin audit-log READ + EXPORT. Runs in the NORMAL resolved-tenant context. The
/// <c>audit_logs</c> table has NO EF global query filter (its TenantId is nullable and shared with system-scoped
/// rows), so EVERY query here filters EXPLICITLY by <see cref="ITenantContext.TenantId"/> (AC-1/FR-1) — this is
/// the deliberate tenant scoping the story calls out.
///
/// <para>Sensitive values are masked on read (FR-4) by <see cref="SensitiveFieldMasker"/>; the export shape is
/// produced by the pure <see cref="AuditLogExporter"/>. The export action audits itself (BR-4). The table is
/// append-only by code convention (AC-5/NFR-3) — there is no update/delete path. PostgreSQL RLS + DB-role
/// UPDATE/DELETE revocation are DEFERRED platform infra.</para>
/// </summary>
public sealed class AuditLogService : IAuditLogService
{
    /// <summary>
    /// FR-5: above this many matching rows the export WOULD be handed to the async Hangfire+email path. That
    /// path is DEFERRED (no email/blob wired), so today the call still returns synchronously with the result's
    /// <c>Deferred</c> flag set so the client can surface the "we'd normally email this" UX.
    /// </summary>
    public const int LargeExportThreshold = 10_000;

    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(
        AppDbContext db,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<AuditLogService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ── List (AC-1/AC-2/FR-1) ──────────────────────────────────────────────────

    public async Task<Result<AuditLogPageDto>> ListAsync(
        AuditLogFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<AuditLogPageDto>.Failure("No tenant context.", 400);

        page = page < 1 ? 1 : page;
        pageSize = NormalizePageSize(pageSize);

        var query = BuildFilteredQuery(filter);

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditRow(
                a.Id, a.CreatedAt, a.UserId, a.Action, a.EventType,
                a.ResourceType, a.ResourceId, a.IpAddress, a.Before, a.After, a.Detail))
            .ToListAsync(cancellationToken);

        var actors = await ResolveActorsAsync(rows.Select(r => r.UserId), cancellationToken);
        var retentionDays = await GetRetentionDaysAsync(cancellationToken);

        var items = rows.Select(r => ToListItem(r, actors)).ToList();

        return Result<AuditLogPageDto>.Success(
            new AuditLogPageDto(items, page, pageSize, total, retentionDays));
    }

    // ── Detail (AC-3/FR-2/FR-3/FR-4) ───────────────────────────────────────────

    public async Task<Result<AuditLogDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<AuditLogDetailDto>.Failure("No tenant context.", 400);

        var tenantId = _tenantContext.TenantId;

        var a = await _db.AuditLogs
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Id == id)
            .Select(x => new AuditDetailRow(
                x.Id, x.CreatedAt, x.UserId, x.Action, x.EventType, x.ResourceType, x.ResourceId,
                x.Before, x.After, x.IpAddress, x.UserAgent, x.TraceId, x.ActorEmployeeNo))
            .FirstOrDefaultAsync(cancellationToken);

        if (a is null)
            return Result<AuditLogDetailDto>.Failure("Audit record not found.", 404);

        var actors = await ResolveActorsAsync(new[] { a.UserId }, cancellationToken);
        actors.TryGetValue(a.UserId ?? Guid.Empty, out var actor);

        var dto = new AuditLogDetailDto(
            a.Id,
            a.CreatedAt,
            a.UserId,
            actor?.Name,
            actor?.Email,
            a.ActorEmployeeNo,
            a.Action ?? a.EventType,
            a.ResourceType,
            a.ResourceId,
            SensitiveFieldMasker.Mask(a.Before),
            SensitiveFieldMasker.Mask(a.After),
            a.IpAddress,
            a.UserAgent,
            a.TraceId);

        return Result<AuditLogDetailDto>.Success(dto);
    }

    // ── Export (AC-4/BR-4/FR-5) ────────────────────────────────────────────────

    public async Task<Result<AuditLogExportResult>> ExportAsync(
        AuditLogFilter filter, AuditLogExportFormat format, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<AuditLogExportResult>.Failure("No tenant context.", 400);

        var query = BuildFilteredQuery(filter);

        var total = await query.CountAsync(cancellationToken);

        // FR-5: above the threshold the async Hangfire+email path WOULD take over. DEFERRED — we still return
        // the file synchronously, flagging Deferred so the client can surface the "emailed link" UX later.
        var deferred = total > LargeExportThreshold;

        var rows = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Select(a => new AuditRow(
                a.Id, a.CreatedAt, a.UserId, a.Action, a.EventType,
                a.ResourceType, a.ResourceId, a.IpAddress, a.Before, a.After, a.Detail))
            .ToListAsync(cancellationToken);

        var actors = await ResolveActorsAsync(rows.Select(r => r.UserId), cancellationToken);
        var items = rows.Select(r => ToListItem(r, actors)).ToList();

        var result = AuditLogExporter.Export(items, format, deferred, DateTime.UtcNow);

        // BR-4: the export action itself is audited so silent data exfiltration is impossible.
        await WriteExportAuditAsync(filter, format, total, cancellationToken);

        _logger.LogInformation(
            "Tenant {TenantId} exported {Count} audit records as {Format} (deferred={Deferred})",
            _tenantContext.TenantId, total, format, deferred);

        return Result<AuditLogExportResult>.Success(result);
    }

    // ── Shared query construction (same filters for list + export, AC-4) ───────

    private IQueryable<AuditLog> BuildFilteredQuery(AuditLogFilter filter)
    {
        var tenantId = _tenantContext.TenantId;

        // EXPLICIT tenant scope — audit_logs has no global query filter (AC-1/FR-1).
        var query = _db.AuditLogs.AsNoTracking().Where(a => a.TenantId == tenantId);

        if (filter.StartDate is { } start)
            query = query.Where(a => a.CreatedAt >= start);

        if (filter.EndDate is { } end)
            query = query.Where(a => a.CreatedAt <= end);

        if (filter.ActorUserId is { } actorId)
            query = query.Where(a => a.UserId == actorId);

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            var action = filter.Action.Trim();
            query = query.Where(a => a.Action == action || a.EventType == action);
        }

        if (!string.IsNullOrWhiteSpace(filter.ResourceType))
        {
            var resourceType = filter.ResourceType.Trim();
            query = query.Where(a => a.ResourceType == resourceType);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchQuery))
        {
            var q = filter.SearchQuery.Trim();
            query = query.Where(a =>
                (a.Before != null && a.Before.Contains(q)) ||
                (a.After != null && a.After.Contains(q)) ||
                (a.Detail != null && a.Detail.Contains(q)));
        }

        return query;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static int NormalizePageSize(int pageSize)
    {
        if (pageSize <= 0)
            return DefaultPageSize;
        return pageSize > MaxPageSize ? MaxPageSize : pageSize;
    }

    /// <summary>
    /// Resolves actor display name + email for the given user ids in ONE query. Users is a GLOBAL entity (no
    /// tenant filter), so the lookup is a plain id-set query. Done as a separate dictionary lookup rather than a
    /// navigation projection because AuditLog has no User navigation (and to avoid the InMemory filtered-nav
    /// projection pitfall).
    /// </summary>
    private async Task<Dictionary<Guid, ActorInfo>> ResolveActorsAsync(
        IEnumerable<Guid?> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, ActorInfo>();

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToListAsync(cancellationToken);

        return users.ToDictionary(u => u.Id, u => new ActorInfo(u.DisplayName, u.Email));
    }

    private async Task<int> GetRetentionDaysAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var retention = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => (int?)t.AuditLogRetentionDays)
            .FirstOrDefaultAsync(cancellationToken);

        return retention ?? 90;
    }

    private static AuditLogListItemDto ToListItem(AuditRow r, IReadOnlyDictionary<Guid, ActorInfo> actors)
    {
        ActorInfo? actor = null;
        if (r.UserId is { } uid)
            actors.TryGetValue(uid, out actor);

        return new AuditLogListItemDto(
            r.Id,
            r.CreatedAt,
            r.UserId,
            actor?.Name,
            actor?.Email,
            r.Action ?? r.EventType,
            r.ResourceType,
            r.ResourceId,
            r.IpAddress,
            BuildSummary(r));
    }

    /// <summary>
    /// A short, MASKED human summary for the list row (AC-1). Prefers the masked after-state, falling back to
    /// before-state, then the free-text Detail. Truncated so the list stays compact.
    /// </summary>
    private static string BuildSummary(AuditRow r)
    {
        var source = !string.IsNullOrWhiteSpace(r.After) ? r.After
            : !string.IsNullOrWhiteSpace(r.Before) ? r.Before
            : r.Detail;

        var masked = SensitiveFieldMasker.Mask(source) ?? string.Empty;
        return Truncate(masked, 280);
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";

    /// <summary>BR-4: write the AuditLog.Export audit row, recording the filters + format + record count.</summary>
    private async Task WriteExportAuditAsync(
        AuditLogFilter filter, AuditLogExportFormat format, int recordCount, CancellationToken cancellationToken)
    {
        var detail =
            $"format={format}; records={recordCount}; " +
            $"start={filter.StartDate:O}; end={filter.EndDate:O}; actor={filter.ActorUserId}; " +
            $"action={filter.Action}; resourceType={filter.ResourceType}; search={filter.SearchQuery}";

        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = "AuditLog.Export",
            Action = "AuditLog.Export",
            ResourceType = "AuditLog",
            Detail = detail,
            CreatedAt = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    // Compact projection rows + actor cache value.
    private sealed record AuditRow(
        Guid Id, DateTime CreatedAt, Guid? UserId, string? Action, string EventType,
        string? ResourceType, string? ResourceId, string? IpAddress, string? Before, string? After, string? Detail);

    private sealed record AuditDetailRow(
        Guid Id, DateTime CreatedAt, Guid? UserId, string? Action, string EventType,
        string? ResourceType, string? ResourceId, string? Before, string? After,
        string? IpAddress, string? UserAgent, string? TraceId, string? ActorEmployeeNo);

    private sealed record ActorInfo(string? Name, string? Email);
}
