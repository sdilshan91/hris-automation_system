using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Enums;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// 360-degree reviewer configuration service (US-PRF-005 AC-1/FR-1/FR-2). HR (Performance.Review.All)
/// configures who reviews an employee: Self + Manager are auto-assigned, Peers (same department) and Direct
/// Reports (org tree) are auto-suggested and can be manually added/removed. BR-2 (the reviewee can never be
/// their own Peer) and FR-3 (configurable minimum reviewers per category) are enforced here. Every read/write
/// is tenant-scoped via the EF global query filter (NFR-2). Notifying reviewers on entering the 360 phase
/// (AC-2) is also driven here via the notification seam.
/// </summary>
public interface IReviewerAssignmentService
{
    /// <summary>
    /// Returns the reviewer-configuration view for a reviewee + cycle (AC-1): the persisted assignments plus
    /// the auto-suggested peers/direct-reports the caller can still add. Auto-creates the Self + Manager
    /// assignments and the suggested-peer/report assignments on first access if none exist yet (FR-2).
    /// </summary>
    Task<Result<ReviewerConfigurationDto>> GetConfigurationAsync(
        Guid revieweeEmployeeId, Guid cycleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually adds a reviewer in a category (AC-1/FR-2). Enforces BR-2 (no self-as-peer), the 360-enabled
    /// gate (BR-1) and BR-3 de-duplication. Returns the created assignment.
    /// </summary>
    Task<Result<ReviewerAssignmentDto>> AddReviewerAsync(
        Guid revieweeEmployeeId, Guid cycleId, Guid reviewerEmployeeId, ReviewerCategory category,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a reviewer assignment (AC-1/FR-2). Cannot remove one that has already submitted.</summary>
    Task<Result> RemoveReviewerAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies every assigned reviewer for a reviewee + cycle that the 360 phase has started (AC-2). Stamps
    /// NotifiedAt and dispatches via the notification seam. Returns the number of reviewers notified.
    /// </summary>
    Task<Result<int>> NotifyReviewersAsync(
        Guid revieweeEmployeeId, Guid cycleId, CancellationToken cancellationToken = default);
}
