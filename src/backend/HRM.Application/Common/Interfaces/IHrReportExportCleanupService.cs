using HRM.Application.Common.Models;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-RPT-004 (BR-3): the testable core of the report-export retention cleanup. Marks every Completed export whose
/// 7-day <c>ExpiresAt</c> has passed as <c>Expired</c> and best-effort DELETES its stored file. Cross-tenant (a
/// system/admin operation), driven by a Hangfire recurring job; the core is exposed here so it can be exercised
/// directly in tests (InMemory-safe). Mirrors the US-ADM-010 IExportCleanupService.
/// </summary>
public interface IHrReportExportCleanupService
{
    /// <summary>Expires + deletes all report exports past their 7-day window. Returns the number expired.</summary>
    Task<Result<int>> ExpireOverdueExportsAsync(CancellationToken cancellationToken = default);
}
