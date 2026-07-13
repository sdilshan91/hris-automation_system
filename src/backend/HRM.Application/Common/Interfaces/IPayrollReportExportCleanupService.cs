using HRM.Application.Common.Models;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// ISSUE-178 PR2: the testable core of the payroll-report-export retention cleanup. Marks every Completed export
/// whose 7-day <c>ExpiresAt</c> has passed as <c>Expired</c> and best-effort DELETES its stored file. Cross-tenant
/// (a system/admin operation), driven by a Hangfire recurring job; the core is exposed here so it can be exercised
/// directly in tests (InMemory-safe). A 1:1 clone of <see cref="IHrReportExportCleanupService"/>.
/// </summary>
public interface IPayrollReportExportCleanupService
{
    /// <summary>Expires + deletes all payroll-report exports past their 7-day window. Returns the number expired.</summary>
    Task<Result<int>> ExpireOverdueExportsAsync(CancellationToken cancellationToken = default);
}
