using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-ADM-004 (AC-4 / FR-3 / NFR-2 / NFR-4): hard-deletes all per-tenant data for a terminating tenant, then
/// marks the tenant <c>Terminated</c> with PII redacted. Runs in a service scope whose tenant context has been
/// restored by the Hangfire job (so the global query filters are scoped to the target tenant), but every
/// delete is ADDITIONALLY scoped by <c>TenantId == tenantId</c> in the predicate as defence in depth — only
/// the target tenant's rows are ever touched (the cross-tenant isolation guarantee).
///
/// <para><b>Generic + ordered.</b> It enumerates every entity type in the EF model that inherits
/// <see cref="BaseEntity"/> (the tenant-scoped business tables) and deletes them CHILD-FIRST, deriving the
/// order from the model's foreign keys via a topological sort (dependents before principals) so FK constraints
/// are respected on a relational provider. <c>UserTenant</c> rows for the tenant are removed too (§7).</para>
///
/// <para><b>Provider-aware (NFR-4 atomic).</b> On a relational provider the whole operation runs inside a DB
/// transaction and uses <c>ExecuteDelete()</c> set-based bulk deletes. The InMemory provider supports neither
/// transactions nor <c>ExecuteDelete</c>, so the service falls back to load-then-<c>RemoveRange</c> + a single
/// SaveChanges — which is what makes the core logic exercisable in the integration tests.</para>
/// </summary>
public sealed class TenantDataDeletionService : ITenantDataDeletionService
{
    private readonly AppDbContext _db;
    private readonly ILogger<TenantDataDeletionService> _logger;
    // BUG-116: invalidates the my-tenants cache entry of every user whose membership is removed here, so a user
    // who belongs to OTHER tenants doesn't keep serving a stale list for up to the 5-min TTL. Optional (nullable,
    // default null) so isolated construction that omits it still compiles; DI injects it in production.
    private readonly IMyTenantsCache? _myTenantsCache;

    public TenantDataDeletionService(
        AppDbContext db,
        ILogger<TenantDataDeletionService> logger,
        IMyTenantsCache? myTenantsCache = null)
    {
        _db = db;
        _logger = logger;
        _myTenantsCache = myTenantsCache;
    }

    public async Task<Result<TenantDeletionSummary>> DeleteTenantDataAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Load the tenant cross-tenant (the service is a system-context operation).
        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure<TenantDeletionSummary>("The tenant does not exist.", 404, "tenant_not_found");

        // Idempotent / defensive: only delete a tenant still in Terminating (a Restore may have intervened).
        if (tenant.Status != TenantStatus.Terminating)
        {
            _logger.LogInformation(
                "TenantDataDeletionService: tenant {TenantId} is {Status}, not Terminating — skipping deletion.",
                tenantId, tenant.Status);
            return Result.Failure<TenantDeletionSummary>(
                "The tenant is no longer scheduled for deletion.", 409, "not_terminating");
        }

        var orderedTypes = GetTenantScopedTypesChildFirst();
        var isRelational = _db.Database.IsRelational();

        // BUG-116: capture the DISTINCT user ids whose memberships are about to be removed, BEFORE the delete —
        // we invalidate each user's my-tenants cache AFTER the transaction commits (so we never evict then fail
        // the write). A user who belongs to other tenants would otherwise serve a stale list until the TTL.
        var affectedUserIds = await _db.UserTenants
            .IgnoreQueryFilters()
            .Where(ut => ut.TenantId == tenantId)
            .Select(ut => ut.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // BUG-252 (BUG-068 class): a user-initiated BeginTransactionAsync throws under Npgsql's retrying
        // execution strategy ("does not support user-initiated transactions") — the whole atomic delete/redact
        // unit (NFR-4) must run *inside* the execution strategy delegate so it is retried as a single retriable
        // unit. All per-attempt accumulators (rowsDeleted, now), the transaction, the deletes, and the tracked
        // lifecycle+audit rows are (re)created inside the delegate so a transient retry starts from a clean
        // state and cannot double-insert. Mirrors ApplicantConversionService.ConvertAsync.
        var strategy = _db.Database.CreateExecutionStrategy();
        var rowsDeleted = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = isRelational
                ? await _db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            long attemptRows = 0;
            var now = DateTime.UtcNow;

            // Lifecycle + audit rows are created *inside* the delegate; on a rolled-back retry we detach them in
            // the catch so the next attempt starts with exactly one of each (no double-insert). AC-4.
            TenantLifecycleEvent? lifecycleEvent = null;
            AuditLog? auditLog = null;

            try
            {
                // Delete every tenant-scoped business table, child-first.
                foreach (var clrType in orderedTypes)
                {
                    attemptRows += await DeleteForTypeAsync(clrType, tenantId, isRelational, cancellationToken);
                }

                // §7: also remove the tenant's UserTenant memberships (not a BaseEntity).
                attemptRows += await DeleteUserTenantsAsync(tenantId, isRelational, cancellationToken);

                // Retain the tenant row, marked Terminated with PII redacted (AC-4). Audit logs + lifecycle events
                // are intentionally retained. These property sets are idempotent, so re-applying them on a retry
                // is safe.
                tenant.Status = TenantStatus.Terminated;
                tenant.Name = "[redacted]";
                tenant.ContactEmail = null;
                tenant.BillingEmail = null;
                tenant.LogoUrl = null;
                tenant.SuspendedReason = null;
                tenant.TerminationScheduledAt = null;
                tenant.UpdatedAt = now;

                var detail = JsonSerializer.Serialize(new
                {
                    EntityTypesDeleted = orderedTypes.Count,
                    RowsDeleted = attemptRows,
                    TerminatedAt = now,
                });
                lifecycleEvent = new TenantLifecycleEvent
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = tenantId,
                    EventType = "terminated",
                    DetailJson = detail,
                    CreatedAt = now,
                    CreatedBy = "system",
                };
                _db.TenantLifecycleEvents.Add(lifecycleEvent);
                auditLog = new AuditLog
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = tenantId,
                    EventType = "Tenant.Terminated",
                    Action = "Tenant.Terminated",
                    ResourceType = "Tenant",
                    ResourceId = tenantId.ToString(),
                    After = detail,
                    CreatedAt = now,
                };
                _db.AuditLogs.Add(auditLog);

                await _db.SaveChangesAsync(cancellationToken);

                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);

                return attemptRows;
            }
            catch
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(cancellationToken);
                // Drop the Added lifecycle/audit rows so a strategy retry doesn't insert them twice.
                if (lifecycleEvent is not null)
                    _db.Entry(lifecycleEvent).State = EntityState.Detached;
                if (auditLog is not null)
                    _db.Entry(auditLog).State = EntityState.Detached;
                throw;
            }
        });

        // BUG-116: memberships are committed-deleted — drop each affected user's cached my-tenants list. Fail-soft
        // per user (the invalidator swallows cache errors), so a Redis blip never fails the completed deletion.
        if (_myTenantsCache is not null)
        {
            foreach (var userId in affectedUserIds)
                await _myTenantsCache.InvalidateAsync(userId, cancellationToken);
        }

        _logger.LogInformation(
            "TenantDataDeletionService: hard-deleted {Rows} row(s) across {Types} type(s) for tenant {TenantId}; " +
            "tenant retained as Terminated with PII redacted.",
            rowsDeleted, orderedTypes.Count, tenantId);

        return Result.Success(new TenantDeletionSummary(tenantId, orderedTypes.Count, rowsDeleted));
    }

    /// <summary>
    /// Deletes all rows of <paramref name="clrType"/> for the tenant. On relational uses set-based
    /// <c>ExecuteDelete</c>; on InMemory falls back to load + RemoveRange (ExecuteDelete is unsupported).
    /// Always tenant-scoped in the predicate (isolation).
    /// </summary>
    private async Task<long> DeleteForTypeAsync(
        Type clrType, Guid tenantId, bool isRelational, CancellationToken cancellationToken)
    {
        if (isRelational)
        {
            // EF.Property-based predicate so this works for any BaseEntity type without a generic call site.
            return await ((IQueryable<BaseEntity>)SetFor(clrType))
                .Where(e => e.TenantId == tenantId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        var rows = await ((IQueryable<BaseEntity>)SetFor(clrType))
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        if (rows.Count > 0)
            _db.RemoveRange(rows);
        return rows.Count;
    }

    private async Task<long> DeleteUserTenantsAsync(Guid tenantId, bool isRelational, CancellationToken cancellationToken)
    {
        if (isRelational)
        {
            return await _db.UserTenants
                .IgnoreQueryFilters()
                .Where(ut => ut.TenantId == tenantId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        var rows = await _db.UserTenants
            .IgnoreQueryFilters()
            .Where(ut => ut.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        if (rows.Count > 0)
            _db.UserTenants.RemoveRange(rows);
        return rows.Count;
    }

    private IQueryable<object> SetFor(Type clrType) => (IQueryable<object>)_db.GetType()
        .GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!
        .MakeGenericMethod(clrType)
        .Invoke(_db, null)!;

    /// <summary>
    /// Returns every CLR type in the EF model that inherits <see cref="BaseEntity"/> (the tenant-scoped
    /// business tables), ordered CHILD-FIRST: a dependent (the entity holding the FK) is deleted before its
    /// principal. The order is derived from the model's foreign keys via Kahn's topological sort — edges run
    /// principal → dependent, and we emit dependents first so deleting them never violates an FK.
    /// </summary>
    private List<Type> GetTenantScopedTypesChildFirst()
    {
        var entityTypes = _db.Model.GetEntityTypes()
            .Where(et => et.ClrType is not null && typeof(BaseEntity).IsAssignableFrom(et.ClrType))
            .ToList();

        var clrTypes = entityTypes.Select(et => et.ClrType).Distinct().ToHashSet();

        // Build dependency edges: principal must be deleted AFTER its dependents → we want dependents first.
        // For each FK, the dependent depends-on the principal for ordering; we sort principals last.
        var dependentsOf = clrTypes.ToDictionary(t => t, _ => new HashSet<Type>()); // principal -> its dependents
        var inDegree = clrTypes.ToDictionary(t => t, _ => 0);                        // # of principals a type depends on

        foreach (var et in entityTypes)
        {
            foreach (var fk in et.GetForeignKeys())
            {
                var dependent = et.ClrType;
                var principal = fk.PrincipalEntityType.ClrType;
                if (principal == dependent) continue;                 // self-ref: ignore for ordering
                if (!clrTypes.Contains(principal)) continue;          // FK to a non-tenant table (Tenant/User): ignore
                if (dependentsOf[principal].Add(dependent))
                    inDegree[dependent]++;
            }
        }

        // Kahn: start with types that depend on nothing (pure principals), append; reverse for child-first.
        var queue = new Queue<Type>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var principalFirst = new List<Type>();
        var localInDegree = new Dictionary<Type, int>(inDegree);

        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            principalFirst.Add(type);
            foreach (var dependent in dependentsOf[type])
            {
                if (--localInDegree[dependent] == 0)
                    queue.Enqueue(dependent);
            }
        }

        // Any remaining types are part of an FK cycle; append them (relational deletes inside a transaction can
        // still cope via deferred constraints, and InMemory has no constraints at all).
        foreach (var type in clrTypes)
            if (!principalFirst.Contains(type))
                principalFirst.Add(type);

        principalFirst.Reverse(); // child-first
        return principalFirst;
    }
}
