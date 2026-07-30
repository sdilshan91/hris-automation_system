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
}
