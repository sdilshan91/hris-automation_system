using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.DataExport;
using HRM.Application.Features.DataExport.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using HRM.Application.Common.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-ADM-010: tenant data-export orchestration + generation.
///
/// <para><b>Initiation</b> (<see cref="InitiateAsync"/> / <see cref="InitiateForTenantAsync"/>) resolves the
/// TARGET tenant (Tenant Admin → the resolved <see cref="ITenantContext.TenantId"/>, ignoring any client tenant
/// id per AC-5; System Admin → an explicit id), runs the BR-2/BR-3 status gate and the BR-5 rate limit, creates a
/// Queued <see cref="ExportRequest"/>, audits "DataExport.Requested", and enqueues generation (Hangfire when
/// wired via the optional <see cref="IExportJobScheduler"/>; otherwise the caller runs <see cref="GenerateAsync"/>
/// directly — which keeps the whole flow testable without Hangfire).</para>
///
/// <para><b>Generation</b> (<see cref="GenerateAsync"/>) sets Processing, then for each selected tenant-scoped
/// entity queries with AsNoTracking (NFR-2) and serializes to BOM'd CSV (auth secrets excluded — BR-7), emits the
/// audit log as JSON Lines, builds manifest.json with REAL SHA-256 checksums (FR-6), ZIPs everything, stores it
/// via the tenant-isolated <see cref="IFileStorage"/> seam, marks the request Completed (+72h expiry), audits
/// "DataExport.Completed", and dispatches the (stub) download-link email to the requester + billing contact.</para>
///
/// <para><b>Cross-tenant safety.</b> Every read re-scopes EXPLICITLY by the target tenant id via
/// IgnoreQueryFilters().Where(TenantId == id) (defence in depth on top of the global filter), so a Tenant A
/// export can never contain Tenant B rows (AC-5).</para>
/// </summary>
public sealed class TenantDataExportService : ITenantDataExportService
{
    private const int DownloadWindowHours = 72;
    private const int MaxExportsPerCalendarMonth = 3;

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _fileStorage;
    private readonly IDataExportNotificationService _notifications;
    private readonly IExportJobScheduler? _scheduler;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TenantDataExportService> _logger;

    public TenantDataExportService(
        AppDbContext db,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IFileStorage fileStorage,
        IDataExportNotificationService notifications,
        ILogger<TenantDataExportService> logger,
        IConfiguration configuration,
        IExportJobScheduler? scheduler = null)
    {
        _db = db;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
        _notifications = notifications;
        _logger = logger;
        _scheduler = scheduler;
        _configuration = configuration;
    }

    // ── Initiation (AC-1 / AC-6) ────────────────────────────────────────────────

    public Task<Result<ExportInitiatedDto>> InitiateAsync(
        ExportInitiateRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Task.FromResult(Result<ExportInitiatedDto>.Failure("No tenant context.", 400));

        // AC-5: ALWAYS the resolved tenant; any client-supplied tenant id is ignored.
        return InitiateCoreAsync(_tenantContext.TenantId, request, isSystemAdmin: false, cancellationToken);
    }

    public Task<Result<ExportInitiatedDto>> InitiateForTenantAsync(
        Guid tenantId, ExportInitiateRequest request, CancellationToken cancellationToken = default)
        => InitiateCoreAsync(tenantId, request, isSystemAdmin: true, cancellationToken);

    private async Task<Result<ExportInitiatedDto>> InitiateCoreAsync(
        Guid tenantId, ExportInitiateRequest request, bool isSystemAdmin, CancellationToken cancellationToken)
    {
        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted, cancellationToken);
        if (tenant is null)
            return Result<ExportInitiatedDto>.Failure("The tenant does not exist.", 404, "tenant_not_found");

        // ── Status gate (BR-2/BR-3) ──
        var gate = CheckStatusGate(tenant.Status, isSystemAdmin);
        if (gate is { } failure)
            return failure;

        // ── Rate limit (BR-5/FR-9) ──
        var now = DateTime.UtcNow;
        var inProgress = await _db.ExportRequests
            .IgnoreQueryFilters()
            .AnyAsync(e => e.TenantId == tenantId && !e.IsDeleted
                && (e.Status == ExportRequestStatus.Queued || e.Status == ExportRequestStatus.Processing),
                cancellationToken);
        if (inProgress)
            return Result<ExportInitiatedDto>.Failure(
                "An export is already in progress for this tenant.", 409, "export_in_progress");

        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var thisMonthCount = await _db.ExportRequests
            .IgnoreQueryFilters()
            .CountAsync(e => e.TenantId == tenantId && !e.IsDeleted && e.RequestedAt >= monthStart, cancellationToken);
        if (thisMonthCount >= MaxExportsPerCalendarMonth)
            return Result<ExportInitiatedDto>.Failure("Monthly export limit reached.", 429, "monthly_limit_reached");

        // ── Create the Queued request ──
        var scope = NormalizeScope(request);
        var requesterId = _currentUser.IsAuthenticated ? _currentUser.UserId : Guid.Empty;

        var export = new ExportRequest
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            Scope = scope,
            Status = ExportRequestStatus.Queued,
            RequestedByUserId = requesterId,
            RequestedAt = now,
            InitiatedBySystemAdmin = isSystemAdmin,
            CreatedAt = now,
            CreatedBy = requesterId == Guid.Empty ? "system" : requesterId.ToString(),
        };
        _db.ExportRequests.Add(export);

        // AC-1 / AC-6: audit the request (note the System Admin actor cross-tenant).
        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            UserId = requesterId == Guid.Empty ? null : requesterId,
            EventType = "DataExport.Requested",
            Action = "DataExport.Requested",
            ResourceType = "ExportRequest",
            ResourceId = export.Id.ToString(),
            Detail = $"scope={scope}; systemAdmin={isSystemAdmin}",
            CreatedAt = now,
        });

        await _db.SaveChangesAsync(cancellationToken);

        // Enqueue generation if Hangfire is wired; otherwise GenerateAsync is invoked directly (job/tests).
        _scheduler?.EnqueueGeneration(export.Id);

        _logger.LogInformation(
            "DataExport {ExportId} requested for tenant {TenantId} (scope={Scope}, systemAdmin={SystemAdmin}).",
            export.Id, tenantId, scope, isSystemAdmin);

        return Result<ExportInitiatedDto>.Success(new ExportInitiatedDto(
            export.Id,
            ExportRequestStatus.Queued.ToString(),
            "Export started. You will receive an email with the download link when it is ready."));
    }

    /// <summary>
    /// BR-2/BR-3 status gate. Allowed: Active/Trial/PastDue/Terminating (AC-4). Suspended: rejected for a Tenant
    /// Admin, ALLOWED for a System Admin (BR-2 "System Admin can initiate on their behalf"). Terminated: rejected
    /// for both. Returns null when allowed, else the failure result.
    /// </summary>
    private static Result<ExportInitiatedDto>? CheckStatusGate(TenantStatus status, bool isSystemAdmin)
    {
        switch (status)
        {
            case TenantStatus.Active:
            case TenantStatus.Trial:
            case TenantStatus.PastDue:
            case TenantStatus.Terminating:
                return null;

            case TenantStatus.Suspended:
                return isSystemAdmin
                    ? null
                    : Result<ExportInitiatedDto>.Failure(
                        "Export is not available while the tenant is suspended. Contact support.",
                        409, "tenant_suspended");

            case TenantStatus.Terminated:
            default:
                return Result<ExportInitiatedDto>.Failure(
                    "Export is not available for a terminated tenant.", 409, "tenant_terminated");
        }
    }

    private static string NormalizeScope(ExportInitiateRequest request)
    {
        if (request.Entities is { Count: > 0 })
            return string.Join(",", request.Entities.Select(e => e.Trim()).Where(e => e.Length > 0));

        var scope = request.Scope?.Trim();
        if (string.IsNullOrWhiteSpace(scope) ||
            string.Equals(scope, ExportEntityRegistry.FullScope, StringComparison.OrdinalIgnoreCase))
            return ExportEntityRegistry.FullScope;

        return scope;
    }

    // ── Generation (AC-2) ───────────────────────────────────────────────────────

    public async Task<Result<ExportRequestDto>> GenerateAsync(
        Guid exportRequestId, CancellationToken cancellationToken = default)
    {
        var export = await _db.ExportRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == exportRequestId && !e.IsDeleted, cancellationToken);
        if (export is null)
            return Result<ExportRequestDto>.Failure("Export request not found.", 404, "export_not_found");

        if (export.Status != ExportRequestStatus.Queued)
            return Result<ExportRequestDto>.Failure(
                $"Export request is {export.Status}, not Queued.", 409, "export_not_queued");

        var tenantId = export.TenantId;
        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        var tenantName = tenant?.Name ?? tenantId.ToString();

        export.Status = ExportRequestStatus.Processing;
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            var (zipBytes, manifest) = await BuildBundleAsync(export, tenantId, tenantName, cancellationToken);

            // Store via the tenant-isolated seam at {tenantId}/exports/{exportId}/export_bundle.zip.
            var relativePath = $"exports/{export.Id}/export_bundle.zip";
            await using (var ms = new MemoryStream(zipBytes, writable: false))
            {
                await _fileStorage.UploadAsync(tenantId, relativePath, ms, "application/zip", cancellationToken);
            }

            var now = DateTime.UtcNow;
            export.Status = ExportRequestStatus.Completed;
            export.CompletedAt = now;
            export.ExpiresAt = now.AddHours(DownloadWindowHours);
            export.FilePath = relativePath;
            export.ManifestJson = manifest.ToJsonString();
            export.RowCountTotal = manifest.Files.Sum(f => f.RowCount);

            _db.AuditLogs.Add(new AuditLog
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                UserId = export.RequestedByUserId == Guid.Empty ? null : export.RequestedByUserId,
                EventType = "DataExport.Completed",
                Action = "DataExport.Completed",
                ResourceType = "ExportRequest",
                ResourceId = export.Id.ToString(),
                After = $"{{\"files\":{manifest.Files.Count},\"rows\":{export.RowCountTotal},\"expiresAt\":\"{export.ExpiresAt:O}\"}}",
                CreatedAt = now,
            });

            await _db.SaveChangesAsync(cancellationToken);

            // AC-2/BR-6: dispatch the (stub) download-link email to the requester + billing contact.
            var recipients = await ResolveRecipientsAsync(export.RequestedByUserId, tenant, cancellationToken);
            // C5/GAP-028: the emailed link used to come from IFileStorage.GetSignedUrl, which despite its name
            // signs nothing — it returns `/files/{tenantId}/{path}`, a scheme NO route has ever served (see
            // LocalFileStorage: "Local dev: return a simple path (no real signing)"). So the "your export is
            // ready" mail for a GDPR Art. 20 portability request led to a 404.
            //
            // It also cannot simply point at the authenticated API endpoint: a link clicked in a mail client
            // carries no Authorization header, so `/api/v1/tenant/data-exports/{id}/download` would 401. The
            // link therefore targets the tenant workspace PAGE, which authenticates the user and then
            // downloads through that endpoint — the same reason B4's avatars had to be fetched, not linked.
            var downloadUrl = BuildExportPageUrl(tenant?.Subdomain, export.Id);
            await _notifications.SendExportReadyAsync(tenantId, export.Id, recipients, downloadUrl, cancellationToken);

            _logger.LogInformation(
                "DataExport {ExportId} completed for tenant {TenantId}: {Files} file(s), {Rows} row(s).",
                export.Id, tenantId, manifest.Files.Count, export.RowCountTotal);

            return Result<ExportRequestDto>.Success(ExportRequestDto.From(export, now));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DataExport {ExportId} failed for tenant {TenantId}.", export.Id, tenantId);

            export.Status = ExportRequestStatus.Failed;
            export.CompletedAt = DateTime.UtcNow;
            export.Error = ex.Message;
            _db.AuditLogs.Add(new AuditLog
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                UserId = export.RequestedByUserId == Guid.Empty ? null : export.RequestedByUserId,
                EventType = "DataExport.Failed",
                Action = "DataExport.Failed",
                ResourceType = "ExportRequest",
                ResourceId = export.Id.ToString(),
                Detail = ex.Message,
                CreatedAt = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync(cancellationToken);

            return Result<ExportRequestDto>.Failure("Export generation failed.", 500, "export_failed");
        }
    }

    /// <summary>The tenant workspace route that lists exports and performs the authenticated download.</summary>
    private const string ExportPagePath = "/admin/data-export";

    /// <summary>
    /// Builds the absolute link the "export ready" email points at:
    /// <c>https://{subdomain}.{baseDomain}/admin/data-export?exportId={id}</c>.
    ///
    /// <para>
    /// Base domain comes from <see cref="PortalLinkBuilder.NormalizeBaseDomain"/> — the existing helper —
    /// rather than a fresh <c>_configuration["Platform:BaseDomain"]</c> read. That lookup is currently
    /// hand-rolled at ~10 sites with three slightly different normalisations; adding an eleventh is how the
    /// next one drifts. (The broader migration is filed, not done here.)
    /// </para>
    ///
    /// <para>
    /// Falls back to a relative path when the tenant has no subdomain, which is better than emitting
    /// <c>https://./…</c> — a malformed absolute URL looks deliverable and is not.
    /// </para>
    /// </summary>
    private string BuildExportPageUrl(string? subdomain, Guid exportId)
    {
        var query = $"{ExportPagePath}?exportId={exportId}";
        if (string.IsNullOrWhiteSpace(subdomain))
        {
            return query;
        }

        var baseDomain = PortalLinkBuilder.NormalizeBaseDomain(_configuration["Platform:BaseDomain"]);
        return $"https://{subdomain.Trim()}.{baseDomain}{query}";
    }

    /// <summary>
    /// Builds the in-memory ZIP bundle + manifest. Selects the entities per the request scope, serializes each to
    /// a BOM'd CSV (auth secrets excluded), adds the Users projection + audit-log JSONL, computes a per-file
    /// SHA-256, and writes the manifest LAST so its own bytes are not part of any checksum.
    /// </summary>
    private async Task<(byte[] Zip, ExportManifest Manifest)> BuildBundleAsync(
        ExportRequest export, Guid tenantId, string tenantName, CancellationToken cancellationToken)
    {
        var delimiter = ResolveDelimiter(export.Scope);
        var selected = ResolveSelectedCodes(export.Scope);

        var files = new List<(string Name, byte[] Bytes, string Entity, int RowCount)>();

        // Curated CSV entities.
        foreach (var entry in ExportEntityRegistry.Entries)
        {
            if (!selected.Full && !selected.Codes.Contains(entry.Code))
                continue;

            var clrType = ResolveClrType(entry.ClrTypeName);
            if (clrType is null)
                continue; // entity not present in the model yet — skip (documented in the registry).

            var (bytes, rowCount) = await SerializeEntityCsvAsync(clrType, tenantId, delimiter, cancellationToken);
            files.Add((entry.FileName, bytes, entry.Code, rowCount));
        }

        // Users projection (name/email/roles only — BR-7).
        if (selected.Full || selected.Codes.Contains(ExportEntityRegistry.UsersCode))
        {
            var (bytes, rowCount) = await SerializeUsersCsvAsync(tenantId, delimiter, cancellationToken);
            files.Add(("users.csv", bytes, ExportEntityRegistry.UsersCode, rowCount));
        }

        // Audit log as JSON Lines.
        if (selected.Full || selected.Codes.Contains(ExportEntityRegistry.AuditLogCode))
        {
            var (bytes, rowCount) = await SerializeAuditLogJsonlAsync(tenantId, cancellationToken);
            files.Add(("audit_log.jsonl", bytes, ExportEntityRegistry.AuditLogCode, rowCount));
        }

        // C5/GAP-028: schema.pdf — the human-readable data dictionary. Art. 20 requires an "intelligible
        // form", and a folder of CSVs with columns like `fte` and `reports_to_employee_id` is machine-readable
        // without being intelligible to the person exercising the right.
        //
        // Built from the header rows of the CSVs ALREADY SERIALIZED above, not from a second reflection pass
        // over the EF model. A second pass would be a second description of one truth and would drift the
        // first time a property was excluded from the CSV writer but not the renderer.
        //
        // Added BEFORE the manifest is computed, so it is checksummed and listed like every other artifact —
        // an unlisted file in the ZIP is exactly the kind of thing an integrity check exists to catch.
        if (files.Count > 0)
        {
            // EVERY file in the bundle, not only the CSVs. Describing just the CSVs meant an audit-log-only
            // partial export shipped a schema.pdf reading "No files were included in this export." while
            // audit_log.jsonl demonstrably was — a document contradicting the bundle it travels in, which is
            // the one thing this renderer exists to prevent. Non-CSV files list no columns because they have
            // none; that is a fact about the file, not an omission.
            var schemas = files
                .Select(f => new ExportSchemaPdfRenderer.FileSchema(
                    f.Name, f.Entity, f.RowCount,
                    f.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                        ? ExportSchemaPdfRenderer.ReadHeaderColumns(f.Bytes, delimiter)
                        : []))
                .ToList();

            var schemaPdf = ExportSchemaPdfRenderer.Render(
                tenantName, export.Id, DateTime.UtcNow, export.Scope, schemas);
            files.Add(("schema.pdf", schemaPdf, "Schema", 0));
        }

        var manifestFiles = files
            .Select(f => new ExportManifestFile(
                f.Name, f.Entity, f.RowCount, f.Bytes.LongLength, ExportManifest.Sha256Hex(f.Bytes)))
            .ToList();

        var manifest = new ExportManifest(
            export.Id, tenantId, tenantName, DateTime.UtcNow, export.Scope, manifestFiles);

        var zip = PackageZip(files, manifest);
        return (zip, manifest);
    }

    private static byte[] PackageZip(
        IReadOnlyList<(string Name, byte[] Bytes, string Entity, int RowCount)> files, ExportManifest manifest)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes, _, _) in files)
                WriteEntry(archive, name, bytes);

            // manifest.json LAST — its bytes are intentionally NOT included in any file's checksum.
            WriteEntry(archive, "manifest.json", manifest.ToJsonBytes());
        }
        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Serializes one tenant-scoped entity to CSV via reflection (auth secrets excluded). Loads the rows with
    /// AsNoTracking + IgnoreQueryFilters + an EXPLICIT TenantId predicate (NFR-2 + AC-5 defence in depth).
    /// </summary>
    private async Task<(byte[] Bytes, int RowCount)> SerializeEntityCsvAsync(
        Type clrType, Guid tenantId, char delimiter, CancellationToken cancellationToken)
    {
        var rows = await LoadTenantRowsAsync(clrType, tenantId, cancellationToken);

        // Invoke CsvSerializer.SerializeEntities<T> for the concrete type.
        var method = typeof(CsvSerializer)
            .GetMethod(nameof(CsvSerializer.SerializeEntities))!
            .MakeGenericMethod(clrType);

        var typedList = ToTypedList(clrType, rows);
        var bytes = (byte[])method.Invoke(null, [typedList, delimiter, null])!;
        return (bytes, rows.Count);
    }

    private async Task<List<object>> LoadTenantRowsAsync(
        Type clrType, Guid tenantId, CancellationToken cancellationToken)
    {
        var set = (IQueryable<BaseEntity>)SetFor(clrType);
        var rows = await set
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && !e.IsDeleted)
            .ToListAsync(cancellationToken);
        return rows.Cast<object>().ToList();
    }

    /// <summary>Users projection: name/email/roles ONLY — never PasswordHash/MfaSecret (BR-7).</summary>
    private async Task<(byte[] Bytes, int RowCount)> SerializeUsersCsvAsync(
        Guid tenantId, char delimiter, CancellationToken cancellationToken)
    {
        // Members of the tenant (UserTenant), then resolve user name/email + role names.
        var memberUserIds = await _db.UserTenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(ut => ut.TenantId == tenantId)
            .Select(ut => ut.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => memberUserIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToListAsync(cancellationToken);

        // Role names per user within this tenant (resolved via UserTenantRole → Role).
        var roleRows = await (
            from utr in _db.UserTenantRoles.AsNoTracking().IgnoreQueryFilters()
            join ut in _db.UserTenants.AsNoTracking().IgnoreQueryFilters() on utr.UserTenantId equals ut.Id
            join r in _db.Roles.AsNoTracking().IgnoreQueryFilters() on utr.RoleId equals r.Id
            where ut.TenantId == tenantId
            select new { ut.UserId, r.Name })
            .ToListAsync(cancellationToken);

        var rolesByUser = roleRows
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => string.Join("; ", g.Select(x => x.Name).Distinct()));

        var projected = users
            .Select(u => new UserExportRow(
                u.DisplayName ?? string.Empty,
                u.Email,
                rolesByUser.GetValueOrDefault(u.Id, string.Empty)))
            .ToList();

        var columns = new List<(string, Func<UserExportRow, object?>)>
        {
            ("name", r => r.Name),
            ("email", r => r.Email),
            ("roles", r => r.Roles),
        };

        var bytes = CsvSerializer.Serialize(projected, columns, delimiter);
        return (bytes, projected.Count);
    }

    private sealed record UserExportRow(string Name, string Email, string Roles);

    /// <summary>
    /// Audit log → JSON Lines (FR-5): one record per line with all fields incl. before/after. Auth secrets in the
    /// before/after JSON are stripped by the deny-list (PII is retained, only auth secrets removed — BR-7).
    /// </summary>
    private async Task<(byte[] Bytes, int RowCount)> SerializeAuditLogJsonlAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        var rows = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new
            {
                a.Id,
                a.CreatedAt,
                a.UserId,
                Action = a.Action ?? a.EventType,
                a.ResourceType,
                a.ResourceId,
                a.IpAddress,
                a.Before,
                a.After,
                a.Detail,
            })
            .ToListAsync(cancellationToken);

        var sb = new StringBuilder();
        foreach (var r in rows)
        {
            var line = JsonSerializer.Serialize(new
            {
                id = r.Id,
                timestamp = r.CreatedAt,
                user_id = r.UserId,
                action = r.Action,
                resource_type = r.ResourceType,
                resource_id = r.ResourceId,
                ip_address = r.IpAddress,
                before = StripAuthSecrets(r.Before),
                after = StripAuthSecrets(r.After),
                detail = r.Detail,
            });
            sb.Append(line).Append('\n');
        }

        return (Encoding.UTF8.GetBytes(sb.ToString()), rows.Count);
    }

    /// <summary>
    /// Strips auth-secret KEYS from a JSON object string (recursively), keeping PII. Returns the input unchanged
    /// when it is not JSON. Reuses the export deny-list (BR-7) — narrower than the audit-log PII masker.
    /// </summary>
    private static string? StripAuthSecrets(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;

        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException) { return json; }
        if (node is null) return json;

        StripNode(node);
        return node.ToJsonString();
    }

    private static void StripNode(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kvp => kvp.Key).ToList())
                {
                    if (ExportSensitiveFields.IsDenied(key))
                    {
                        obj.Remove(key);
                        continue;
                    }
                    var child = obj[key];
                    if (child is not null) StripNode(child);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr)
                    if (item is not null) StripNode(item);
                break;
        }
    }

    private async Task<IReadOnlyList<string>> ResolveRecipientsAsync(
        Guid requesterUserId, Tenant? tenant, CancellationToken cancellationToken)
    {
        var recipients = new List<string>();

        if (requesterUserId != Guid.Empty)
        {
            var email = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == requesterUserId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(email))
                recipients.Add(email);
        }

        // BR-6: also the tenant's billing contact.
        var billing = tenant?.BillingEmail ?? tenant?.ContactEmail;
        if (!string.IsNullOrWhiteSpace(billing) && !recipients.Contains(billing))
            recipients.Add(billing);

        return recipients;
    }

    private sealed record SelectedScope(bool Full, HashSet<string> Codes);

    private static SelectedScope ResolveSelectedCodes(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope) ||
            string.Equals(scope, ExportEntityRegistry.FullScope, StringComparison.OrdinalIgnoreCase))
            return new SelectedScope(true, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var codes = scope
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new SelectedScope(false, codes);
    }

    // Delimiter is encoded into the scope today only as the default ',' — kept as a hook for future format opts.
    private static char ResolveDelimiter(string _) => ',';

    private Type? ResolveClrType(string clrTypeName)
        => _db.Model.GetEntityTypes()
            .Select(et => et.ClrType)
            .FirstOrDefault(t => t is not null
                && t.Name == clrTypeName
                && typeof(BaseEntity).IsAssignableFrom(t));

    private IQueryable<object> SetFor(Type clrType) => (IQueryable<object>)_db.GetType()
        .GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!
        .MakeGenericMethod(clrType)
        .Invoke(_db, null)!;

    private static object ToTypedList(Type clrType, List<object> rows)
    {
        var listType = typeof(List<>).MakeGenericType(clrType);
        var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
        foreach (var row in rows)
            list.Add(row);
        return list;
    }

    // ── History / status / download (FR-7/FR-9) ─────────────────────────────────

    public async Task<Result<IReadOnlyList<ExportRequestDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<ExportRequestDto>>.Failure("No tenant context.", 400);

        var now = DateTime.UtcNow;
        var rows = await _db.ExportRequests
            .AsNoTracking()
            .OrderByDescending(e => e.RequestedAt)
            .ToListAsync(cancellationToken);

        var dtos = rows.Select(e => ExportRequestDto.From(e, now)).ToList();
        return Result<IReadOnlyList<ExportRequestDto>>.Success(dtos);
    }

    public async Task<Result<ExportRequestDto>> GetAsync(Guid exportRequestId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ExportRequestDto>.Failure("No tenant context.", 400);

        var export = await _db.ExportRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == exportRequestId, cancellationToken);
        if (export is null)
            return Result<ExportRequestDto>.Failure("Export request not found.", 404, "export_not_found");

        return Result<ExportRequestDto>.Success(ExportRequestDto.From(export, DateTime.UtcNow));
    }

    public async Task<Result<ExportDownloadDto>> DownloadAsync(
        Guid exportRequestId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ExportDownloadDto>.Failure("No tenant context.", 400);

        var export = await _db.ExportRequests
            .FirstOrDefaultAsync(e => e.Id == exportRequestId, cancellationToken);
        if (export is null)
            return Result<ExportDownloadDto>.Failure("Export request not found.", 404, "export_not_found");

        var now = DateTime.UtcNow;

        if (export.Status == ExportRequestStatus.Expired
            || (export.ExpiresAt is { } exp && exp <= now))
            return Result<ExportDownloadDto>.Failure(
                "The export download link has expired and the file has been deleted.", 410, "export_expired");

        if (export.Status != ExportRequestStatus.Completed || string.IsNullOrWhiteSpace(export.FilePath))
            return Result<ExportDownloadDto>.Failure(
                "The export is not yet available for download.", 409, "export_not_ready");

        await using var stream = await _fileStorage.OpenReadAsync(export.TenantId, export.FilePath, cancellationToken);
        if (stream is null)
            return Result<ExportDownloadDto>.Failure(
                "The export file is no longer available.", 410, "export_file_missing");

        await using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);

        // AC-3/NFR-4: audit the download.
        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = export.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = "DataExport.Downloaded",
            Action = "DataExport.Downloaded",
            ResourceType = "ExportRequest",
            ResourceId = export.Id.ToString(),
            CreatedAt = now,
        });
        await _db.SaveChangesAsync(cancellationToken);

        return Result<ExportDownloadDto>.Success(new ExportDownloadDto(
            ms.ToArray(), "application/zip", $"export_bundle_{export.Id}.zip"));
    }
}
