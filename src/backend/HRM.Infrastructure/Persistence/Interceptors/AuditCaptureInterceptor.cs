using System.Diagnostics;
using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.AuditLog;
using HRM.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRM.Infrastructure.Persistence.Interceptors;

/// <summary>
/// US-NTF-004: EF Core <see cref="SaveChangesInterceptor"/> that AUTOMATICALLY captures generic
/// INSERT / UPDATE / DELETE operations on OPT-IN business entities (those implementing
/// <see cref="IAuditableEntity"/>) into the SHARED <see cref="AuditLog"/> table, with property-level
/// before/after diffs. This is the missing piece the existing audit infra did not provide — the
/// <c>AuditInterceptor</c> only stamps <see cref="BaseEntity"/> audit fields + impersonation attribution,
/// and the various services (Auth, Payroll, Employee field-audit, Admin) write only their OWN explicit
/// audit rows. There is intentionally NO second audit table (FR-1/FR-2).
///
/// <para><b>Design / ordering.</b> Registered AFTER <c>TenantInterceptor</c> and <c>AuditInterceptor</c>,
/// so by the time this runs in <c>SavingChanges(Async)</c> the new entities already have their UUIDv7
/// <c>Id</c> stamped (resource id is known) and <c>TenantId</c> set. The generated <see cref="AuditLog"/>
/// rows are added to the SAME <see cref="DbContext"/> and committed in the SAME save — so a rolled-back
/// business write leaves no orphan audit row, and inserts' resource ids are always available (no second
/// SaveChanges needed).</para>
///
/// <para><b>Recursion guard.</b> <see cref="AuditLog"/>, <see cref="EmployeeFieldAuditLog"/> and any other
/// audit/log infrastructure entity are never captured (they don't implement <see cref="IAuditableEntity"/>
/// anyway) — so writing audit rows never generates more audit rows.</para>
///
/// <para><b>Rules.</b> BR-2: <c>Before</c> is null for INSERT, <c>After</c> is null for DELETE. A soft-delete
/// (<c>IsDeleted</c> false→true) is captured as a "{Entity}.Delete" with before/after on the changed flags
/// (AC-3). BR-3: only CHANGED properties are captured for UPDATE. FR-7/FR-8: tenant, actor, IP, user-agent
/// and trace id are enriched from <see cref="ITenantContext"/> / <see cref="ICurrentUser"/> / the ambient
/// HTTP request / <see cref="Activity.Current"/> — mirroring how the explicit writers (e.g. PayrollAuditLogger)
/// enrich their rows.</para>
/// </summary>
public sealed class AuditCaptureInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public AuditCaptureInterceptor(
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Capture(DbContext? context)
    {
        if (context is null)
            return;

        // Snapshot first: building the audit rows below mutates the change tracker (adds AuditLog entries),
        // so we must not enumerate it lazily while adding.
        var auditable = context.ChangeTracker
            .Entries<IAuditableEntity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (auditable.Count == 0)
            return;

        var enrichment = ResolveEnrichment();
        var now = DateTime.UtcNow;

        var rows = new List<AuditLog>(auditable.Count);
        foreach (var entry in auditable)
        {
            var row = BuildRow(entry, enrichment, now);
            if (row is not null)
                rows.Add(row);
        }

        foreach (var row in rows)
            context.Add(row);
    }

    private AuditLog? BuildRow(EntityEntry entry, Enrichment enrichment, DateTime now)
    {
        var entityName = entry.Entity.GetType().Name;
        var resourceId = ResolveResourceId(entry);

        string action;
        string? before;
        string? after;

        switch (entry.State)
        {
            case EntityState.Added:
                action = $"{entityName}.Create";
                before = null;                          // BR-2: no before for INSERT
                after = SerializeAll(entry);
                break;

            case EntityState.Deleted:
                action = $"{entityName}.Delete";
                before = SerializeAll(entry);
                after = null;                           // BR-2: no after for DELETE
                break;

            case EntityState.Modified:
            {
                var (changedBefore, changedAfter) = SerializeChanged(entry);
                // Nothing materially changed (e.g. only audit-field shadow churn) → skip the row.
                if (changedBefore is null && changedAfter is null)
                    return null;

                // AC-3: a soft-delete (IsDeleted false→true) is logically a DELETE, not an UPDATE.
                action = IsSoftDelete(entry) ? $"{entityName}.Delete" : $"{entityName}.Update";
                before = changedBefore;
                after = changedAfter;
                break;
            }

            default:
                return null;
        }

        return new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = enrichment.TenantId,
            UserId = enrichment.UserId,
            ActorEmployeeNo = enrichment.ActorEmail,    // best-effort actor display (no employee_no in claims)
            // Keep EventType in sync with Action so the legacy (EventType-based) reads still see the entry.
            EventType = action,
            Action = action,
            ResourceType = entityName,
            ResourceId = resourceId,
            Before = before,
            After = after,
            IpAddress = enrichment.IpAddress,
            UserAgent = enrichment.UserAgent,
            TraceId = enrichment.TraceId,
            CreatedAt = now,
        };
    }

    /// <summary>AC-3: a soft-delete is an <c>IsDeleted</c> transition false → true.</summary>
    private static bool IsSoftDelete(EntityEntry entry)
    {
        var prop = entry.Properties.FirstOrDefault(p => p.Metadata.Name == nameof(BaseEntity.IsDeleted));
        if (prop is null)
            return false;

        return prop.OriginalValue is false && prop.CurrentValue is true;
    }

    /// <summary>
    /// Value equality used for the change diff. <see cref="object.Equals(object?, object?)"/> works for scalars
    /// and strings; reference-typed values (e.g. collection navigations mapped as a column) fall back to a
    /// string comparison so a same-content list is not falsely reported as changed.
    /// </summary>
    private static bool ValuesEqual(object? a, object? b)
    {
        if (Equals(a, b))
            return true;
        if (a is null || b is null)
            return false;
        if (a is string || b is string)
            return false;
        // Last-resort structural compare for complex/collection-backed columns.
        return string.Equals(a.ToString(), b.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Full snapshot of all mapped, non-ignored scalar properties (used for INSERT/DELETE).
    /// BUG-281: the serialized JSON is passed through <see cref="SensitiveFieldMasker.Mask"/> at WRITE time so
    /// PII (e.g. bank account, national id, salary) is never persisted in cleartext in the audit before/after
    /// JSONB — the read-path masking in AuditLogService is no longer the only line of defence.
    /// </summary>
    internal static string? SerializeAll(EntityEntry entry)
    {
        var map = new Dictionary<string, object?>();
        foreach (var prop in entry.Properties)
        {
            if (ShouldSkipProperty(prop.Metadata.Name))
                continue;
            map[prop.Metadata.Name] = entry.State == EntityState.Deleted ? prop.OriginalValue : prop.CurrentValue;
        }

        return map.Count == 0 ? null : SensitiveFieldMasker.Mask(Serialize(map));
    }

    /// <summary>
    /// BR-3: capture ONLY the properties whose value actually changed, into separate before/after maps.
    /// Returns (null, null) when no meaningful property changed.
    /// BUG-281: both the before and after JSON are passed through <see cref="SensitiveFieldMasker.Mask"/> at
    /// WRITE time so PII field values are redacted before they are persisted to the audit table.
    /// </summary>
    internal static (string? before, string? after) SerializeChanged(EntityEntry entry)
    {
        var before = new Dictionary<string, object?>();
        var after = new Dictionary<string, object?>();

        foreach (var prop in entry.Properties)
        {
            if (ShouldSkipProperty(prop.Metadata.Name))
                continue;
            // BR-3: a property is "changed" when its original and current values differ. We compare values
            // directly (rather than relying solely on IsModified) so the diff is robust across EF providers.
            if (ValuesEqual(prop.OriginalValue, prop.CurrentValue))
                continue;

            before[prop.Metadata.Name] = prop.OriginalValue;
            after[prop.Metadata.Name] = prop.CurrentValue;
        }

        if (before.Count == 0)
            return (null, null);

        return (SensitiveFieldMasker.Mask(Serialize(before)), SensitiveFieldMasker.Mask(Serialize(after)));
    }

    /// <summary>
    /// Audit-trail noise we never record: the audit bookkeeping columns themselves change on (almost) every
    /// write and carry no business meaning for the diff.
    /// </summary>
    private static bool ShouldSkipProperty(string name) => name switch
    {
        nameof(BaseEntity.CreatedAt) => true,
        nameof(BaseEntity.CreatedBy) => true,
        nameof(BaseEntity.UpdatedAt) => true,
        nameof(BaseEntity.UpdatedBy) => true,
        _ => false,
    };

    private static string ResolveResourceId(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
            return string.Empty;

        var parts = key.Properties
            .Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? string.Empty);

        return string.Join(":", parts);
    }

    private Enrichment ResolveEnrichment()
    {
        var http = _httpContextAccessor?.HttpContext;

        Guid? userId = _currentUser.IsAuthenticated && _currentUser.UserId != Guid.Empty
            ? _currentUser.UserId
            : null;

        string? actorEmail = _currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(_currentUser.Email)
            ? _currentUser.Email
            : null;

        var ua = http?.Request.Headers.UserAgent.ToString();

        return new Enrichment(
            TenantId: _tenantContext.IsResolved ? _tenantContext.TenantId : null,
            UserId: userId,
            ActorEmail: actorEmail,
            IpAddress: http?.Connection.RemoteIpAddress?.ToString(),
            UserAgent: string.IsNullOrWhiteSpace(ua) ? null : Truncate(ua, 500),
            // FR-7: distributed trace id — prefer the active Activity, fall back to the request trace identifier.
            TraceId: Activity.Current?.Id ?? http?.TraceIdentifier);
    }

    private static string Serialize(Dictionary<string, object?> map)
    {
        try
        {
            return JsonSerializer.Serialize(map, JsonOptions);
        }
        catch
        {
            // Audit must never break the business operation; degrade to an empty object on a non-serializable value.
            return "{}";
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private readonly record struct Enrichment(
        Guid? TenantId,
        Guid? UserId,
        string? ActorEmail,
        string? IpAddress,
        string? UserAgent,
        string? TraceId);
}
