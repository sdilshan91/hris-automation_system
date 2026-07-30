using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Persistence;

/// <summary>
/// The single source of truth for a tenant's cumulative stored-bytes usage (US-ADM-012 AC-3/AC-4).
///
/// <para>ISSUE-340: the storage-quota gate historically summed only <c>EmployeeDocuments.FileSizeBytes</c>, so
/// report exports, payroll-report exports and generated payslip PDFs did not count toward the tenant's quota —
/// real usage was undercounted and a tenant could exceed its contracted storage without tripping the gate. This
/// helper widens the sum to ALL four size-bearing tables. The quota gate (<c>EnforceStorageQuotaAsync</c>) and
/// the future AC-4 usage gauge MUST both read this ONE method so the enforced number and the displayed number
/// cannot drift apart.</para>
///
/// <para>Every table is tenant-scoped by the EF global query filter, so the caller must invoke this with a
/// resolved tenant context; no explicit tenant predicate is needed (or wanted — that would bypass the filter).</para>
/// </summary>
public static class TenantStorageUsage
{
    /// <summary>Sums stored bytes across every size-bearing table for the CURRENT tenant.</summary>
    public static async Task<long> ComputeBytesAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var documentBytes = await db.EmployeeDocuments
            .SumAsync(d => (long?)d.FileSizeBytes, cancellationToken) ?? 0L;
        var hrReportBytes = await db.HrReportExports
            .SumAsync(e => (long?)e.FileSizeBytes, cancellationToken) ?? 0L;
        var payrollReportBytes = await db.PayrollReportExports
            .SumAsync(e => (long?)e.FileSizeBytes, cancellationToken) ?? 0L;
        var payslipBytes = await db.PayrollSlips
            .SumAsync(s => (long?)s.PdfFileSizeBytes, cancellationToken) ?? 0L;

        return documentBytes + hrReportBytes + payrollReportBytes + payslipBytes;
    }

    /// <summary>
    /// The CROSS-TENANT companion to <see cref="ComputeBytesAsync"/> for the system/admin monitoring dashboard
    /// (US-ADM-012 AC-4): sums stored bytes across the SAME four size-bearing tables/columns, but grouped by
    /// <c>TenantId</c> via <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{T}"/> — because the
    /// platform-monitoring service runs with no resolved tenant (the tenant query filter is disabled there, so
    /// the current-tenant <see cref="ComputeBytesAsync"/> would sum EVERY tenant into one bucket).
    ///
    /// <para>Kept in THIS class, right beside <see cref="ComputeBytesAsync"/>, so the "which four tables / which
    /// size column" definition lives in ONE place and the enforced quota total and the displayed gauge cannot
    /// drift apart (ISSUE-340). If a fifth size-bearing table is added, BOTH methods must gain it.</para>
    /// </summary>
    /// <param name="onlyTenant">When set, restricts the scan to a single tenant (the detail view); otherwise all tenants.</param>
    public static async Task<Dictionary<Guid, long>> ComputeBytesByTenantAsync(
        AppDbContext db, Guid? onlyTenant = null, CancellationToken cancellationToken = default)
    {
        var totals = new Dictionary<Guid, long>();

        void Merge(IEnumerable<TenantBytes> rows)
        {
            foreach (var r in rows)
                totals[r.TenantId] = totals.GetValueOrDefault(r.TenantId) + r.Bytes;
        }

        Merge(await db.EmployeeDocuments.IgnoreQueryFilters()
            .Where(x => onlyTenant == null || x.TenantId == onlyTenant)
            .GroupBy(x => x.TenantId)
            .Select(g => new TenantBytes(g.Key, g.Sum(x => (long?)x.FileSizeBytes) ?? 0L))
            .ToListAsync(cancellationToken));
        Merge(await db.HrReportExports.IgnoreQueryFilters()
            .Where(x => onlyTenant == null || x.TenantId == onlyTenant)
            .GroupBy(x => x.TenantId)
            .Select(g => new TenantBytes(g.Key, g.Sum(x => (long?)x.FileSizeBytes) ?? 0L))
            .ToListAsync(cancellationToken));
        Merge(await db.PayrollReportExports.IgnoreQueryFilters()
            .Where(x => onlyTenant == null || x.TenantId == onlyTenant)
            .GroupBy(x => x.TenantId)
            .Select(g => new TenantBytes(g.Key, g.Sum(x => (long?)x.FileSizeBytes) ?? 0L))
            .ToListAsync(cancellationToken));
        Merge(await db.PayrollSlips.IgnoreQueryFilters()
            .Where(x => onlyTenant == null || x.TenantId == onlyTenant)
            .GroupBy(x => x.TenantId)
            .Select(g => new TenantBytes(g.Key, g.Sum(x => (long?)x.PdfFileSizeBytes) ?? 0L))
            .ToListAsync(cancellationToken));

        return totals;
    }

    private readonly record struct TenantBytes(Guid TenantId, long Bytes);
}
