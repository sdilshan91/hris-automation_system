// ============================================================================
// US-NTF-001: In-app notification read + mark-read service unit tests.
// Covers: paginated panel list (20/page, most-recent-first, unread + total counts),
// unread-count badge, single mark-as-read with OWNERSHIP enforcement (AC-4),
// mark-all-as-read (AC-5), and cross-tenant / cross-user isolation (AC-6/BR-1).
// Uses the EF Core InMemory provider (mirrors HolidayServiceTests).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class NotificationReadServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    public NotificationReadServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);
        _currentUser.IsAuthenticated.Returns(true);
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private NotificationReadService CreateService()
        => new(CreateDbContext(), _tenantContext, _currentUser);

    private Notification NewNotification(
        Guid? userId = null, Guid? tenantId = null, bool isRead = false, DateTime? createdAt = null,
        string type = "leave_approved") => new()
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId ?? _tenantId,
            UserId = userId ?? _userId,
            Type = type,
            Title = "Title",
            Message = "Message",
            IsRead = isRead,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };

    private void Seed(params Notification[] notifications)
    {
        using var db = CreateDbContext();
        db.Notifications.AddRange(notifications);
        db.SaveChanges();
    }

    // ── Pagination (AC-3/FR-6) ──────────────────────────────────────────────

    [Fact]
    public async Task GetPage_ReturnsFirst20_MostRecentFirst_WithCounts()
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var notifications = Enumerable.Range(0, 25)
            .Select(i => NewNotification(createdAt: baseTime.AddMinutes(i), isRead: i < 5))
            .ToArray();
        Seed(notifications);

        var result = await CreateService().GetPageAsync(page: 1, pageSize: 20);

        result.IsSuccess.Should().BeTrue();
        var page = result.Value!;
        page.Items.Should().HaveCount(20);
        page.TotalCount.Should().Be(25);
        page.UnreadCount.Should().Be(20); // 25 total, 5 read
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(20);

        // Most-recent-first: the newest (minute 24) is first.
        page.Items.First().CreatedAt.Should().Be(baseTime.AddMinutes(24));
        page.Items.Should().BeInDescendingOrder(i => i.CreatedAt);
    }

    [Fact]
    public async Task GetPage_SecondPage_ReturnsRemainder()
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Seed(Enumerable.Range(0, 25)
            .Select(i => NewNotification(createdAt: baseTime.AddMinutes(i)))
            .ToArray());

        var result = await CreateService().GetPageAsync(page: 2, pageSize: 20);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(5);
        result.Value.Page.Should().Be(2);
    }

    [Fact]
    public async Task GetPage_DefaultsPageSizeTo20_WhenZeroOrNegative()
    {
        Seed(Enumerable.Range(0, 30).Select(_ => NewNotification()).ToArray());

        var result = await CreateService().GetPageAsync(page: 1, pageSize: 0);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PageSize.Should().Be(20);
        result.Value.Items.Should().HaveCount(20);
    }

    [Fact]
    public async Task GetPage_OnlyReturnsCallersOwnNotifications()
    {
        var otherUser = Guid.NewGuid();
        Seed(
            NewNotification(userId: _userId),
            NewNotification(userId: _userId),
            NewNotification(userId: otherUser));

        var result = await CreateService().GetPageAsync(page: 1, pageSize: 20);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(2);
        result.Value.Items.Should().HaveCount(2);
    }

    // ── Unread count (FR-5) ─────────────────────────────────────────────────

    [Fact]
    public async Task GetUnreadCount_CountsOnlyUnreadForCaller()
    {
        var otherUser = Guid.NewGuid();
        Seed(
            NewNotification(isRead: false),
            NewNotification(isRead: false),
            NewNotification(isRead: true),
            NewNotification(userId: otherUser, isRead: false)); // other user's unread excluded

        var result = await CreateService().GetUnreadCountAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Count.Should().Be(2);
    }

    // ── Mark single read + ownership (AC-4) ─────────────────────────────────

    [Fact]
    public async Task MarkRead_SetsIsReadAndReadAt()
    {
        var notification = NewNotification(isRead: false);
        Seed(notification);

        var result = await CreateService().MarkReadAsync(notification.Id);

        result.IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        var stored = db.Notifications.Single(n => n.Id == notification.Id);
        stored.IsRead.Should().BeTrue();
        stored.ReadAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkRead_OtherUsersNotification_Returns404_AndDoesNotMutate()
    {
        var otherUser = Guid.NewGuid();
        var notification = NewNotification(userId: otherUser, isRead: false);
        Seed(notification);

        var result = await CreateService().MarkReadAsync(notification.Id);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);

        using var db = CreateDbContext();
        db.Notifications.IgnoreQueryFilters().Single(n => n.Id == notification.Id).IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task MarkRead_UnknownId_Returns404()
    {
        var result = await CreateService().MarkReadAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── Mark all read (AC-5) ────────────────────────────────────────────────

    [Fact]
    public async Task MarkAllRead_MarksOnlyCallersUnread_AndReturnsCount()
    {
        var otherUser = Guid.NewGuid();
        Seed(
            NewNotification(isRead: false),
            NewNotification(isRead: false),
            NewNotification(isRead: true),
            NewNotification(userId: otherUser, isRead: false)); // untouched

        var result = await CreateService().MarkAllReadAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Updated.Should().Be(2);

        using var db = CreateDbContext();
        db.Notifications.Count(n => n.UserId == _userId && !n.IsRead).Should().Be(0);
        db.Notifications.IgnoreQueryFilters().Count(n => n.UserId == otherUser && !n.IsRead).Should().Be(1);
    }

    [Fact]
    public async Task MarkAllRead_NoUnread_ReturnsZero()
    {
        Seed(NewNotification(isRead: true));

        var result = await CreateService().MarkAllReadAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Updated.Should().Be(0);
    }

    // ── Tenant isolation (AC-6/NFR-2) ───────────────────────────────────────

    [Fact]
    public async Task GetPage_DoesNotLeakOtherTenantsNotifications()
    {
        // Seed a notification for the SAME user id but a DIFFERENT tenant, written via a context whose
        // tenant context is that other tenant (so the global query filter is the only thing keeping it out).
        var otherTenantId = Guid.NewGuid();
        var otherTenantContext = Substitute.For<ITenantContext>();
        otherTenantContext.TenantId.Returns(otherTenantId);
        otherTenantContext.IsResolved.Returns(true);

        using (var otherDb = TestDbContextFactory.Create(otherTenantContext, _dbName))
        {
            otherDb.Notifications.Add(new Notification
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = otherTenantId,
                UserId = _userId,
                Type = "leave_approved",
                Title = "Other tenant",
                Message = "Should not be visible",
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
            });
            otherDb.SaveChanges();
        }

        Seed(NewNotification()); // one in the caller's tenant

        var result = await CreateService().GetPageAsync(page: 1, pageSize: 20);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        result.Value.Items.Should().OnlyContain(i => i.Title != "Other tenant");
    }
}
