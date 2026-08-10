using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.AuditLog;
using HRM.Application.Features.AuditLog.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-ADM-008: Tenant-Admin audit-log READ + EXPORT. Runs in the NORMAL resolved-tenant context. EVERY query
/// here filters EXPLICITLY by <see cref="ITenantContext.TenantId"/> (AC-1/FR-1) — the deliberate tenant scoping
/// the story calls out, and still the primary control here.
///
/// <para>GAP-006: <c>audit_logs</c> gained an EF global query filter (it previously had none). It is the one
/// filter with a <c>TenantId == null</c> arm, because this table's TenantId is nullable and shared with
/// system-scoped rows. That does NOT relax the explicit scoping above — the filter is a floor beneath it, not
/// a replacement for it, and the two are deliberately redundant.</para>
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

    /// <summary>US-NTF-005 FR-2: cap on the actor type-ahead result set.</summary>
    private const int ActorSearchMaxLimit = 20;

    /// <summary>US-NTF-005 FR-9/BR-5: the meta-audit action name written when the audit log is viewed.</summary>
    private const string MetaAuditViewAction = "AuditLog.View";

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AuditLogService> _logger;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public AuditLogService(
        AppDbContext db,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<AuditLogService> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _db = db;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    // ── List (AC-1/AC-2/FR-1) ──────────────────────────────────────────────────

    public async Task<Result<AuditLogPageDto>> ListAsync(
        AuditLogFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<AuditLogPageDto>.Failure("No tenant context.", 400);

        // ISSUE-012: reject an inverted date range (start > end) with 400 rather than silently applying two
        // independent >=/<= predicates that no row can satisfy → a misleading 200/empty result. Same
        // "validate inputs, don't silently no-op" family as ISSUE-003.
        if (filter.StartDate is { } start && filter.EndDate is { } end && start > end)
            return Result<AuditLogPageDto>.Failure(
                "The start date must not be after the end date.", 400, "invalid_date_range");

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

        // US-NTF-005 FR-9/BR-5: viewing the audit log is itself an audited event (meta-audit). ONE row per LIST
        // request (not per Get) so it can't spam. This is a plain AuditLog INSERT (Action="AuditLog.View") — it
        // does NOT go through ListAsync, so it can never recurse / trigger another view-audit.
        await WriteViewAuditAsync(filter, page, pageSize, total, cancellationToken);

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

        // BUG-085: created_at is timestamptz — Npgsql rejects a DateTime with Kind=Unspecified (which a
        // date-only ?startDate=2026-07-01 binds to) with a 500. Normalize any Kind to UTC so a bare-date bound
        // is treated as UTC-midnight instead of throwing (shared by the list + export paths).
        if (filter.StartDate is { } start)
        {
            var startUtc = start.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(start, DateTimeKind.Utc) : start.ToUniversalTime();
            query = query.Where(a => a.CreatedAt >= startUtc);
        }

        if (filter.EndDate is { } end)
        {
            var endUtc = end.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(end, DateTimeKind.Utc) : end.ToUniversalTime();
            query = query.Where(a => a.CreatedAt <= endUtc);
        }

        if (filter.ActorUserId is { } actorId)
            query = query.Where(a => a.UserId == actorId);

        // US-NTF-005 FR-2: multi-select action filter (IN-list, OR within the group). Back-compat: the singular
        // filter.Action is folded in as one more OR-member, so US-ADM-008 single-value callers/tests still work.
        var actions = CombineValues(filter.Action, filter.Actions);
        if (actions.Count > 0)
            query = query.Where(a =>
                (a.Action != null && actions.Contains(a.Action)) || actions.Contains(a.EventType));

        // US-NTF-005 FR-2: multi-select resource-type filter (IN-list). Singular filter.ResourceType folded in.
        var resourceTypes = CombineValues(filter.ResourceType, filter.ResourceTypes);
        if (resourceTypes.Count > 0)
            query = query.Where(a => a.ResourceType != null && resourceTypes.Contains(a.ResourceType));

        if (!string.IsNullOrWhiteSpace(filter.SearchQuery))
        {
            var q = filter.SearchQuery.Trim();

            // The human-readable text/structured columns are searched identically on both providers
            // (`.Contains` → LIKE on Postgres, client-side match on InMemory — both case-sensitive).
            if (_db.Database.IsNpgsql())
            {
                // US-NTF-005 FR-2 / BUG-241: keyword search MUST also cover the before/after JSONB content.
                // Before/After are `jsonb` on Postgres, and a bare `.Contains`/ILIKE on a jsonb column is NOT
                // translatable — it 500s with `operator does not exist: jsonb ~~ text` (that was BUG-007, why
                // before/after were dropped from the predicate). LINQ has no jsonb→text cast operator, so we
                // express `before::text ILIKE '%q%' OR after::text ILIKE '%q%'` via a parameterized, tenant-scoped
                // FromSql fragment that yields the matching row ids, and OR that into the predicate. ILIKE ⇒ the
                // jsonb-content match is case-INsensitive (the other text fields stay case-sensitive via LIKE).
                // LIKE wildcards in the term (`%`/`_`, common in jsonb keys like "bank_account_number") are escaped
                // so they match literally, mirroring EF's auto-escaping of `.Contains`.
                var pattern = "%" + EscapeLikePattern(q) + "%";
                var jsonbMatchIds = _db.AuditLogs
                    .FromSql($@"SELECT * FROM audit_logs WHERE tenant_id = {tenantId} AND (before::text ILIKE {pattern} OR after::text ILIKE {pattern})")
                    .Select(a => a.Id);

                query = query.Where(a =>
                    (a.Detail != null && a.Detail.Contains(q)) ||
                    (a.Action != null && a.Action.Contains(q)) ||
                    a.EventType.Contains(q) ||
                    (a.ResourceType != null && a.ResourceType.Contains(q)) ||
                    (a.ResourceId != null && a.ResourceId.Contains(q)) ||
                    jsonbMatchIds.Contains(a.Id));
            }
            else
            {
                // InMemory (test) provider: Before/After are plain strings here (not jsonb), so match them
                // client-side with `.Contains`. NOTE the case-sensitivity seam: this `.Contains` is case-SENSITIVE
                // whereas the Postgres branch above uses case-INsensitive ILIKE for before/after. FromSql is not
                // supported by the InMemory provider, hence the separate branch.
                query = query.Where(a =>
                    (a.Detail != null && a.Detail.Contains(q)) ||
                    (a.Action != null && a.Action.Contains(q)) ||
                    a.EventType.Contains(q) ||
                    (a.ResourceType != null && a.ResourceType.Contains(q)) ||
                    (a.ResourceId != null && a.ResourceId.Contains(q)) ||
                    (a.Before != null && a.Before.Contains(q)) ||
                    (a.After != null && a.After.Contains(q)));
            }
        }

        // US-NTF-005 FR-9/BR-5: the AuditLog.View meta-audit rows (written on every list view) are accountability
        // records, NOT operational audit events. Exclude them from the default list/export so the viewer doesn't
        // become self-referential noise (every page load would otherwise inflate the next load's counts). They are
        // still persisted + tenant-scoped + directly queryable WHEN the caller explicitly asks for them via the
        // action filter (so a forensic "who viewed the audit log" query still works). This also keeps US-ADM-008's
        // exact-count assertions intact (those callers never request "AuditLog.View").
        if (!actions.Contains(MetaAuditViewAction))
            query = query.Where(a => a.Action != MetaAuditViewAction && a.EventType != MetaAuditViewAction);

        return query;
    }

    // ── Actor autocomplete (US-NTF-005 FR-2) ──────────────────────────────────

    public async Task<Result<IReadOnlyList<AuditLogActorDto>>> SearchActorsAsync(
        string? search, int limit, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<AuditLogActorDto>>.Failure("No tenant context.", 400);

        limit = limit is <= 0 or > ActorSearchMaxLimit ? ActorSearchMaxLimit : limit;
        var tenantId = _tenantContext.TenantId;

        // Distinct actor user ids that appear in THIS tenant's audit log (explicit tenant scope, FR-1).
        var actorIds = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.UserId != null)
            .Select(a => a.UserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (actorIds.Count == 0)
            return Result<IReadOnlyList<AuditLogActorDto>>.Success(Array.Empty<AuditLogActorDto>());

        // Resolve name/email from the global users table, applying the type-ahead match server-side.
        var usersQuery = _db.Users
            .AsNoTracking()
            .Where(u => actorIds.Contains(u.Id));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            usersQuery = usersQuery.Where(u =>
                (u.DisplayName != null && u.DisplayName.Contains(q)) ||
                (u.Email != null && u.Email.Contains(q)));
        }

        var actors = await usersQuery
            .OrderBy(u => u.DisplayName)
            .Take(limit)
            .Select(u => new AuditLogActorDto(u.Id, u.DisplayName, u.Email))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<AuditLogActorDto>>.Success(actors);
    }

    // ── Filter options (US-NTF-005 FR-2) ──────────────────────────────────────

    public async Task<Result<AuditLogFilterOptionsDto>> GetFilterOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<AuditLogFilterOptionsDto>.Failure("No tenant context.", 400);

        var tenantId = _tenantContext.TenantId;
        var scoped = _db.AuditLogs.AsNoTracking().Where(a => a.TenantId == tenantId);

        // Prefer the structured Action, falling back to the legacy EventType, mirroring the list/detail behavior.
        var actions = await scoped
            .Select(a => a.Action ?? a.EventType)
            .Where(v => v != null && v != "")
            .Distinct()
            .OrderBy(v => v)
            .ToListAsync(cancellationToken);

        var resourceTypes = await scoped
            .Where(a => a.ResourceType != null && a.ResourceType != "")
            .Select(a => a.ResourceType!)
            .Distinct()
            .OrderBy(v => v)
            .ToListAsync(cancellationToken);

        return Result<AuditLogFilterOptionsDto>.Success(
            new AuditLogFilterOptionsDto(actions!, resourceTypes));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// US-NTF-005 FR-2: folds the back-compat singular value and the new multi-select array into one distinct,
    /// trimmed, non-empty list for IN-list filtering. Returns an empty list when neither is supplied.
    /// </summary>
    private static List<string> CombineValues(string? single, IReadOnlyList<string>? many)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(single))
            values.Add(single.Trim());
        if (many is not null)
        {
            foreach (var v in many)
                if (!string.IsNullOrWhiteSpace(v))
                    values.Add(v.Trim());
        }
        return values.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Escapes the SQL-LIKE wildcard metacharacters (<c>\</c>, <c>%</c>, <c>_</c>) in a raw search term so the
    /// FromSql <c>ILIKE</c> pattern (BUG-241 before/after jsonb search) matches them literally — mirroring EF's
    /// automatic escaping of translated <c>string.Contains</c>. Uses the PostgreSQL default LIKE escape char (<c>\</c>).
    /// </summary>
    private static string EscapeLikePattern(string term)
        => term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

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

    /// <summary>
    /// US-NTF-005 FR-9/BR-5: write the AuditLog.View meta-audit row for a LIST request, recording the actor,
    /// tenant, request IP/UA/trace, and the filters that were applied. Best-effort: a failure to write the
    /// meta-audit row must never fail the user's read (the page result is already computed). One row per request;
    /// this is a plain insert so it can never recurse into ListAsync.
    /// </summary>
    private async Task WriteViewAuditAsync(
        AuditLogFilter filter, int page, int pageSize, int total, CancellationToken cancellationToken)
    {
        var http = _httpContextAccessor?.HttpContext;

        var actions = CombineValues(filter.Action, filter.Actions);
        var resourceTypes = CombineValues(filter.ResourceType, filter.ResourceTypes);

        var detail =
            $"page={page}; pageSize={pageSize}; results={total}; " +
            $"start={filter.StartDate:O}; end={filter.EndDate:O}; actor={filter.ActorUserId}; " +
            $"actions=[{string.Join(',', actions)}]; resourceTypes=[{string.Join(',', resourceTypes)}]; " +
            $"search={filter.SearchQuery}";

        try
        {
            _db.AuditLogs.Add(new AuditLog
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
                EventType = MetaAuditViewAction,
                Action = MetaAuditViewAction,
                ResourceType = "AuditLog",
                Detail = detail,
                IpAddress = http?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = UserAgentOf(http),
                TraceId = http?.TraceIdentifier,
                CreatedAt = DateTime.UtcNow,
            });

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Never let the meta-audit failure surface as a failed audit-log view.
            _logger.LogWarning(ex, "Failed to write AuditLog.View meta-audit row for tenant {TenantId}.",
                _tenantContext.TenantId);
        }
    }

    private static string? UserAgentOf(HttpContext? http)
    {
        if (http is null) return null;
        var ua = http.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(ua) ? null : (ua.Length <= 500 ? ua : ua[..500]);
    }

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
