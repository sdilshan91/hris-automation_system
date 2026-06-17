using HRM.Application.Common.Models;
using HRM.Application.Features.Notifications.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Read + mark-read operations for the current user's notifications (US-NTF-001 AC-3/AC-4/AC-5/FR-5/FR-6/FR-7).
/// Runs in the NORMAL resolved-tenant request scope: every operation is scoped to the calling user
/// (<see cref="ICurrentUser.UserId"/>) within the resolved tenant (EF global query filter + explicit user filter),
/// so a user can only ever see or mutate their own notifications (BR-1, AC-6 cross-tenant isolation).
/// </summary>
public interface INotificationReadService
{
    /// <summary>Paginated list of the caller's notifications, most-recent-first, with unread + total counts (AC-3/FR-6).</summary>
    Task<Result<NotificationPageDto>> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>The caller's unread-notification count for the bell badge (FR-5).</summary>
    Task<Result<UnreadCountDto>> GetUnreadCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark a single notification read by id — only when it belongs to the caller (AC-4). Returns 404 when the
    /// notification does not exist or is owned by another user/tenant (no information leak).
    /// </summary>
    Task<Result> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default);

    /// <summary>Mark ALL of the caller's unread notifications read; returns how many rows changed (AC-5).</summary>
    Task<Result<MarkAllReadResultDto>> MarkAllReadAsync(CancellationToken cancellationToken = default);
}
