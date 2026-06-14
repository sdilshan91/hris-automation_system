using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveCarryForward.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Leave carry-forward and expiry processing (US-LV-008). Drives the two Hangfire jobs
/// (year-end carry-forward and monthly expiry) and the read-only preview API. All operations
/// are tenant-scoped via ITenantContext + EF global query filters.
///
/// The year-end job and the preview share one pure calculation (LeaveCarryForwardCalculator)
/// so the preview cannot diverge from what the job actually does.
/// </summary>
public interface ILeaveCarryForwardService
{
    /// <summary>
    /// FR-2 / AC-1 / AC-2 / AC-4: processes year-end carry-forward and forfeiture for every
    /// employee × applicable leave type in the current tenant for the supplied closing
    /// <paramref name="fromYear"/>. Writes CarryForward / Expired (or Encashed, BR-5) ledger
    /// entries and a LeaveCarryForwardTracking row per processed pair.
    ///
    /// Idempotent (NFR-3): skips any pair that already has a tracking row for fromYear → fromYear+1.
    /// Returns the number of carry-forward tracking rows created.
    /// </summary>
    Task<Result<int>> ProcessYearEndAsync(int fromYear, CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-3 / AC-3 / BR-3: expires carried-forward days that remain unconsumed past their expiry
    /// date for the current tenant. For each Active tracking row whose expiry date has passed,
    /// creates an Expired ledger entry for the FIFO-remaining carried days (BR-4) and marks the
    /// tracking row Expired. Idempotent: a row is only processed once.
    /// Returns the number of tracking rows expired.
    /// </summary>
    Task<Result<int>> ProcessExpiryAsync(
        DateOnly asOf, CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-5 / AC-5: read-only preview of the projected carry-forward and forfeiture per employee ×
    /// leave type for the supplied closing <paramref name="fromYear"/>. Commits nothing.
    /// Uses the SAME calculation as <see cref="ProcessYearEndAsync"/>.
    /// </summary>
    Task<Result<IReadOnlyList<CarryForwardPreviewDto>>> PreviewYearEndAsync(
        int fromYear, CancellationToken cancellationToken = default);
}
