using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Persistence;

/// <summary>
/// The single source of truth for a tenant's month-to-date API-call count (US-PLT-004), read off the
/// <see cref="TenantApiUsage"/> aggregate, and the atomic writer the background flusher uses to persist buffered
/// deltas.
///
/// <para><b>Read</b> (<see cref="CountThisMonthByTenantAsync"/>): grouped by <c>TenantId</c> with
/// <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{T}"/> so the cross-tenant, no-resolved-tenant
/// monitoring service reads a correct per-tenant count — mirrors <see cref="TenantEmailSendUsage"/>. The reported
/// number lags real-time by at most one flush interval (buffered, not-yet-flushed increments are excluded); this
/// is acceptable for an advisory usage gauge.</para>
///
/// <para><b>Write</b> (<see cref="UpsertAsync"/>): an <c>INSERT … ON CONFLICT (tenant_id, year_month) DO UPDATE
/// SET call_count = call_count + EXCLUDED.call_count</c> per delta. The addition happens INSIDE the database, so
/// two concurrent flushes both applying deltas to the same row cannot lose counts — a read-modify-write in
/// application code would. It runs cross-tenant on the flusher's no-resolved-tenant context, which routes to the
/// privileged (BYPASSRLS) connection under RLS, so a single writer can span every tenant's row.</para>
/// </summary>
public static class TenantApiCallUsage
{
    /// <summary>
    /// Counts API calls whose bucket is the UTC month containing <paramref name="nowUtc"/>, grouped by tenant.
    /// </summary>
    /// <param name="onlyTenant">When set, restricts to a single tenant (the detail view); otherwise all tenants.</param>
    public static async Task<Dictionary<Guid, long>> CountThisMonthByTenantAsync(
        AppDbContext db, DateTime nowUtc, Guid? onlyTenant = null, CancellationToken cancellationToken = default)
    {
        var yearMonth = TenantApiUsage.ToYearMonth(nowUtc);

        var rows = await db.TenantApiUsages.IgnoreQueryFilters()
            .Where(u => !u.IsDeleted
                     && u.YearMonth == yearMonth
                     && (onlyTenant == null || u.TenantId == onlyTenant))
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Sum(x => x.CallCount) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.TenantId, r => r.Count);
    }

    /// <summary>
    /// Persists the buffered deltas with a per-row atomic upsert. Idempotent under concurrency: the count is
    /// incremented by the database, never overwritten.
    /// </summary>
    public static async Task UpsertAsync(
        AppDbContext db, IReadOnlyCollection<ApiCallCountDelta> deltas, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        foreach (var d in deltas)
        {
            if (d.Count == 0L)
                continue;

            var id = BaseEntity.NewUuidV7();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tenant_api_usage (id, tenant_id, year_month, call_count, created_at, is_deleted)
                VALUES ({id}, {d.TenantId}, {d.YearMonth}, {d.Count}, {nowUtc}, false)
                ON CONFLICT (tenant_id, year_month)
                DO UPDATE SET call_count = tenant_api_usage.call_count + EXCLUDED.call_count,
                              updated_at = {nowUtc}
                """, cancellationToken);
        }
    }
}
