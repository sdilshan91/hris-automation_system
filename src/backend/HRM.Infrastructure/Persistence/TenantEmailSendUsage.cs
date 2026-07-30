using HRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Persistence;

/// <summary>
/// The single source of truth for a tenant's month-to-date SENT-email count (US-ADM-012 AC-4), read off the
/// <see cref="Domain.Entities.NotificationDelivery"/> ledger.
///
/// <para>The counted predicate is deliberately narrow and MUST stay identical wherever it is used (the AC-4
/// usage gauge here, and any future <c>max_email_sends_per_month</c> enforcement gate) so the displayed number
/// and any enforced number cannot drift apart — the same anti-drift rationale as <see cref="TenantStorageUsage"/>:
/// <list type="bullet">
///   <item><see cref="Domain.Entities.NotificationDelivery.Channel"/> == <see cref="NotificationDeliveryChannel.Email"/>
///     (in-app rows never consume the email quota),</item>
///   <item><see cref="Domain.Entities.NotificationDelivery.Status"/> == <see cref="NotificationDeliveryStatus.Sent"/>
///     (only a successful send counts — Queued/Failed/Suppressed/Deferred do not), and</item>
///   <item><see cref="Domain.Entities.NotificationDelivery.SentAt"/> &gt;= the start of the current UTC month.</item>
/// </list></para>
///
/// <para>Grouped by <c>TenantId</c> with <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{T}"/>
/// so the cross-tenant, no-resolved-tenant monitoring service reads a correct per-tenant count (the tenant query
/// filter is disabled in the system/admin context — mirrors <see cref="TenantStorageUsage.ComputeBytesByTenantAsync"/>).</para>
/// </summary>
public static class TenantEmailSendUsage
{
    /// <summary>
    /// Counts Email + Sent deliveries whose <c>SentAt</c> falls in the UTC month containing <paramref name="nowUtc"/>,
    /// grouped by tenant.
    /// </summary>
    /// <param name="onlyTenant">When set, restricts the scan to a single tenant (the detail view); otherwise all tenants.</param>
    public static async Task<Dictionary<Guid, int>> CountSentThisMonthByTenantAsync(
        AppDbContext db, DateTime nowUtc, Guid? onlyTenant = null, CancellationToken cancellationToken = default)
    {
        var monthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var rows = await db.NotificationDeliveries.IgnoreQueryFilters()
            .Where(d => d.Channel == NotificationDeliveryChannel.Email
                     && d.Status == NotificationDeliveryStatus.Sent
                     && d.SentAt >= monthStart
                     && (onlyTenant == null || d.TenantId == onlyTenant))
            .GroupBy(d => d.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.TenantId, r => r.Count);
    }
}
