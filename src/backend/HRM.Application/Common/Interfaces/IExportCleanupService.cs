using HRM.Application.Common.Models;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ADM-010 (FR-7/NFR-4): the testable core of the export-retention cleanup. Marks every Completed export whose
/// 72h <c>ExpiresAt</c> has passed as <c>Expired</c>, DELETES its stored bundle via the file-storage seam, and
/// audits the expiry. Cross-tenant (a system/admin operation), driven by a Hangfire recurring job; the core is
/// exposed here so it can be exercised directly in tests (InMemory-safe).
/// </summary>
public interface IExportCleanupService
{
    /// <summary>Expires + deletes all exports past their download window. Returns the number expired.</summary>
    Task<Result<int>> ExpireOverdueExportsAsync(CancellationToken cancellationToken = default);
}
