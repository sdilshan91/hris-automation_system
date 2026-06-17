using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Notifications.DTOs;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-NTF-001 read + mark-read service. Runs in the resolved-tenant request scope; every operation is
/// scoped to the calling user (<see cref="ICurrentUser.UserId"/>) within the resolved tenant — the EF
/// global query filter handles tenant isolation (AC-6) and an explicit <c>UserId</c> filter handles
/// per-user ownership (BR-1/AC-4). A user can never read or mutate another user's notifications.
/// </summary>
public sealed class NotificationReadService : INotificationReadService
{
    public const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    public NotificationReadService(AppDbContext db, ITenantContext tenantContext, ICurrentUser currentUser)
    {
        _db = db;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    public async Task<Result<NotificationPageDto>> GetPageAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<NotificationPageDto>.Failure("No tenant context.", 400);

        var userId = _currentUser.UserId;
        if (userId == Guid.Empty)
            return Result<NotificationPageDto>.Failure("Not authenticated.", 401);

        page = page < 1 ? 1 : page;
        pageSize = NormalizePageSize(pageSize);

        // Tenant isolation is the global query filter; the explicit UserId scopes to the caller (BR-1).
        var mine = _db.Notifications.AsNoTracking().Where(n => n.UserId == userId);

        var totalCount = await mine.CountAsync(cancellationToken);
        var unreadCount = await mine.CountAsync(n => !n.IsRead, cancellationToken);

        var items = await mine
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationDto(
                n.Id, n.Type, n.Title, n.Message, n.ResourceType, n.ResourceId, n.IsRead, n.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result<NotificationPageDto>.Success(
            new NotificationPageDto(items, unreadCount, totalCount, page, pageSize));
    }

    public async Task<Result<UnreadCountDto>> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<UnreadCountDto>.Failure("No tenant context.", 400);

        var userId = _currentUser.UserId;
        if (userId == Guid.Empty)
            return Result<UnreadCountDto>.Failure("Not authenticated.", 401);

        var count = await _db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);

        return Result<UnreadCountDto>.Success(new UnreadCountDto(count));
    }

    public async Task<Result> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("No tenant context.", 400);

        var userId = _currentUser.UserId;
        if (userId == Guid.Empty)
            return Result.Failure("Not authenticated.", 401);

        // Ownership is enforced in the predicate: a non-owned/cross-tenant/non-existent id all fall through
        // to the 404 path, so we never reveal that someone else's notification exists (AC-4).
        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, cancellationToken);

        if (notification is null)
            return Result.Failure("Notification not found.", 404);

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result<MarkAllReadResultDto>> MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<MarkAllReadResultDto>.Failure("No tenant context.", 400);

        var userId = _currentUser.UserId;
        if (userId == Guid.Empty)
            return Result<MarkAllReadResultDto>.Failure("Not authenticated.", 401);

        var unread = await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync(cancellationToken);

        if (unread.Count == 0)
            return Result<MarkAllReadResultDto>.Success(new MarkAllReadResultDto(0));

        var now = DateTime.UtcNow;
        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Result<MarkAllReadResultDto>.Success(new MarkAllReadResultDto(unread.Count));
    }

    private static int NormalizePageSize(int pageSize)
    {
        if (pageSize <= 0)
            return DefaultPageSize;
        return pageSize > MaxPageSize ? MaxPageSize : pageSize;
    }
}
