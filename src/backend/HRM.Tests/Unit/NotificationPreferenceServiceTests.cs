// ============================================================================
// US-NTF-003: per-user notification preferences service unit tests.
// Covers: matrix merge over defaults (FR-5/BR-1), mandatory category cannot be
// disabled (BR-2/AC-3/AC-4), BR-3 both-channels-off rejection, reset reverts to
// defaults (FR-7), ShouldDeliver respects toggles + mandatory override + quiet-hours
// defer (FR-6/BR-5), invalid-timezone rejection, and tenant/user isolation (AC-5/BR-4).
// Uses the EF Core InMemory provider (mirrors NotificationReadServiceTests).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Notifications;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class NotificationPreferenceServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 6, 17, 12, 0, 0, TimeSpan.Zero));

    public NotificationPreferenceServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);
        _currentUser.IsAuthenticated.Returns(true);
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private NotificationPreferenceService CreateService(TimeProvider? time = null)
        => new(CreateDbContext(), _tenantContext, _currentUser,
            NullLogger<NotificationPreferenceService>.Instance, time ?? _time, cache: null);

    private void SeedPreference(NotificationPreference pref)
    {
        using var db = CreateDbContext();
        db.NotificationPreferences.Add(pref);
        db.SaveChanges();
    }

    // ── Matrix merge over defaults (FR-5/BR-1, AC-1) ────────────────────────

    [Fact]
    public async Task GetMatrix_WithNoSavedPrefs_ReturnsAllDefaults()
    {
        var result = await CreateService().GetMatrixAsync();

        result.IsSuccess.Should().BeTrue();
        var matrix = result.Value!;
        matrix.Categories.Should().HaveCount(NotificationPreferenceDefaults.All.Count);

        // Every category present and matches its default.
        foreach (var def in NotificationPreferenceDefaults.All)
        {
            var row = matrix.Categories.Single(c => c.Category == def.Category.ToString());
            row.CategoryName.Should().Be(def.DisplayName);
            row.IsMandatory.Should().Be(def.IsMandatory);
            if (def.IsMandatory)
            {
                row.ChannelInApp.Should().BeTrue();
                row.ChannelEmail.Should().BeTrue();
            }
            else
            {
                row.ChannelInApp.Should().Be(def.DefaultInApp);
                row.ChannelEmail.Should().Be(def.DefaultEmail);
            }
        }

        matrix.QuietHours.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task GetMatrix_WithSavedOverride_MergesOverDefaults()
    {
        SeedPreference(new NotificationPreference
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            UserId = _userId,
            Category = NotificationCategory.LeaveUpdates,
            ChannelInApp = true,
            ChannelEmail = false, // overridden off
        });

        var result = await CreateService().GetMatrixAsync();

        var leave = result.Value!.Categories.Single(c => c.Category == nameof(NotificationCategory.LeaveUpdates));
        leave.ChannelInApp.Should().BeTrue();
        leave.ChannelEmail.Should().BeFalse();

        // An untouched category still shows its default.
        var payroll = result.Value!.Categories.Single(c => c.Category == nameof(NotificationCategory.PayrollNotifications));
        payroll.ChannelEmail.Should().BeTrue();
    }

    // ── Mandatory cannot be disabled (BR-2/AC-3/AC-4) ───────────────────────

    [Fact]
    public async Task UpdateCategory_Mandatory_DisableEmail_IsRejected()
    {
        var result = await CreateService().UpdateCategoryAsync(
            NotificationCategory.SecurityAlerts, channelInApp: true, channelEmail: false);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("category_mandatory");
    }

    [Fact]
    public async Task UpdateCategory_Mandatory_StaysForcedOn_EvenWhenBothRequestedTrue()
    {
        var result = await CreateService().UpdateCategoryAsync(
            NotificationCategory.SecurityAlerts, channelInApp: true, channelEmail: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ChannelInApp.Should().BeTrue();
        result.Value.ChannelEmail.Should().BeTrue();
        result.Value.IsMandatory.Should().BeTrue();
    }

    // ── BR-3 both-channels-off rejected ─────────────────────────────────────

    [Fact]
    public async Task UpdateCategory_NonMandatory_BothChannelsOff_IsRejected()
    {
        var result = await CreateService().UpdateCategoryAsync(
            NotificationCategory.LeaveUpdates, channelInApp: false, channelEmail: false);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("both_channels_off");
    }

    [Fact]
    public async Task UpdateCategory_NonMandatory_OneChannelOn_Persists()
    {
        var result = await CreateService().UpdateCategoryAsync(
            NotificationCategory.LeaveUpdates, channelInApp: true, channelEmail: false);

        result.IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        var stored = db.NotificationPreferences.Single(p => p.UserId == _userId && p.Category == NotificationCategory.LeaveUpdates);
        stored.ChannelInApp.Should().BeTrue();
        stored.ChannelEmail.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateCategory_Twice_UpdatesSameRow()
    {
        await CreateService().UpdateCategoryAsync(NotificationCategory.LeaveUpdates, true, false);
        await CreateService().UpdateCategoryAsync(NotificationCategory.LeaveUpdates, false, true);

        using var db = CreateDbContext();
        var rows = db.NotificationPreferences.Where(p => p.Category == NotificationCategory.LeaveUpdates).ToList();
        rows.Should().HaveCount(1);
        rows[0].ChannelInApp.Should().BeFalse();
        rows[0].ChannelEmail.Should().BeTrue();
    }

    // ── Reset reverts to defaults (FR-7) ────────────────────────────────────

    [Fact]
    public async Task Reset_DeletesOverrides_AndReturnsDefaultMatrix()
    {
        await CreateService().UpdateCategoryAsync(NotificationCategory.LeaveUpdates, true, false);
        await CreateService().UpdateQuietHoursAsync(true, new TimeOnly(22, 0), new TimeOnly(7, 0), "UTC");

        var result = await CreateService().ResetAsync();

        result.IsSuccess.Should().BeTrue();
        var leave = result.Value!.Categories.Single(c => c.Category == nameof(NotificationCategory.LeaveUpdates));
        leave.ChannelEmail.Should().BeTrue(); // reverted to default (true)
        result.Value.QuietHours.Enabled.Should().BeFalse();

        using var db = CreateDbContext();
        db.NotificationPreferences.Count(p => p.UserId == _userId).Should().Be(0);
    }

    // ── ShouldDeliver: toggles + mandatory override (FR-6/BR-2) ──────────────

    [Fact]
    public async Task ShouldDeliver_DefaultLeaveEmail_DeliversWhenNoOverride()
    {
        var decision = await CreateService().ShouldDeliverAsync(
            _tenantId, _userId, NotificationCategory.LeaveUpdates, NotificationChannel.Email);

        decision.Kind.Should().Be(DeliveryDecisionKind.Deliver);
    }

    [Fact]
    public async Task ShouldDeliver_DisabledEmail_IsSuppressed()
    {
        await CreateService().UpdateCategoryAsync(NotificationCategory.LeaveUpdates, channelInApp: true, channelEmail: false);

        var decision = await CreateService().ShouldDeliverAsync(
            _tenantId, _userId, NotificationCategory.LeaveUpdates, NotificationChannel.Email);

        decision.Kind.Should().Be(DeliveryDecisionKind.Suppressed);
    }

    [Fact]
    public async Task ShouldDeliver_DisabledEmail_InAppStillDelivers()
    {
        await CreateService().UpdateCategoryAsync(NotificationCategory.LeaveUpdates, channelInApp: true, channelEmail: false);

        var decision = await CreateService().ShouldDeliverAsync(
            _tenantId, _userId, NotificationCategory.LeaveUpdates, NotificationChannel.InApp);

        decision.Kind.Should().Be(DeliveryDecisionKind.Deliver);
    }

    [Fact]
    public async Task ShouldDeliver_MandatoryCategory_AlwaysDelivers_EvenIfStoredOff()
    {
        // Force a stored row with both channels off (bypassing the BR-2 guard) to prove the dispatch path
        // still overrides it because the category is mandatory.
        SeedPreference(new NotificationPreference
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            UserId = _userId,
            Category = NotificationCategory.SecurityAlerts,
            ChannelInApp = false,
            ChannelEmail = false,
            IsMandatory = true,
        });

        var email = await CreateService().ShouldDeliverAsync(
            _tenantId, _userId, NotificationCategory.SecurityAlerts, NotificationChannel.Email);
        var inApp = await CreateService().ShouldDeliverAsync(
            _tenantId, _userId, NotificationCategory.SecurityAlerts, NotificationChannel.InApp);

        email.Kind.Should().Be(DeliveryDecisionKind.Deliver);
        inApp.Kind.Should().Be(DeliveryDecisionKind.Deliver);
    }

    // ── Quiet-hours defer logic (BR-5) ──────────────────────────────────────

    [Fact]
    public async Task ShouldDeliver_Email_DuringQuietHours_IsDeferredUntilEnd()
    {
        // Quiet hours 22:00–07:00 UTC; "now" is 23:00 UTC → inside the window.
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 17, 23, 0, 0, TimeSpan.Zero));
        await CreateService(time).UpdateQuietHoursAsync(true, new TimeOnly(22, 0), new TimeOnly(7, 0), "UTC");

        var decision = await CreateService(time).ShouldDeliverAsync(
            _tenantId, _userId, NotificationCategory.LeaveUpdates, NotificationChannel.Email);

        decision.Kind.Should().Be(DeliveryDecisionKind.DeferredUntilQuietHoursEnd);
        // End is 07:00 the NEXT day (overnight window starting before midnight).
        decision.DeferUntilUtc.Should().Be(new DateTime(2026, 6, 18, 7, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task ShouldDeliver_InApp_DuringQuietHours_IsImmediate()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 17, 23, 0, 0, TimeSpan.Zero));
        await CreateService(time).UpdateQuietHoursAsync(true, new TimeOnly(22, 0), new TimeOnly(7, 0), "UTC");

        var decision = await CreateService(time).ShouldDeliverAsync(
            _tenantId, _userId, NotificationCategory.LeaveUpdates, NotificationChannel.InApp);

        decision.Kind.Should().Be(DeliveryDecisionKind.Deliver);
    }

    [Fact]
    public async Task ShouldDeliver_Email_OutsideQuietHours_DeliversImmediately()
    {
        // "now" is 12:00 UTC (the default _time), outside 22:00–07:00.
        await CreateService().UpdateQuietHoursAsync(true, new TimeOnly(22, 0), new TimeOnly(7, 0), "UTC");

        var decision = await CreateService().ShouldDeliverAsync(
            _tenantId, _userId, NotificationCategory.LeaveUpdates, NotificationChannel.Email);

        decision.Kind.Should().Be(DeliveryDecisionKind.Deliver);
    }

    // ── Quiet-hours validation ──────────────────────────────────────────────

    [Fact]
    public async Task UpdateQuietHours_InvalidTimezone_IsRejected()
    {
        var result = await CreateService().UpdateQuietHoursAsync(
            true, new TimeOnly(22, 0), new TimeOnly(7, 0), "Not/AZone");

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("invalid_timezone");
    }

    [Fact]
    public async Task UpdateQuietHours_Disable_ClearsWindow()
    {
        await CreateService().UpdateQuietHoursAsync(true, new TimeOnly(22, 0), new TimeOnly(7, 0), "UTC");

        var result = await CreateService().UpdateQuietHoursAsync(false, null, null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Enabled.Should().BeFalse();

        var matrix = await CreateService().GetMatrixAsync();
        matrix.Value!.QuietHours.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateQuietHours_AppliesToAllUsersRows()
    {
        await CreateService().UpdateCategoryAsync(NotificationCategory.LeaveUpdates, true, false);
        await CreateService().UpdateCategoryAsync(NotificationCategory.AttendanceAlerts, true, false);

        await CreateService().UpdateQuietHoursAsync(true, new TimeOnly(21, 30), new TimeOnly(6, 30), "UTC");

        using var db = CreateDbContext();
        var rows = db.NotificationPreferences.Where(p => p.UserId == _userId).ToList();
        rows.Should().HaveCountGreaterThanOrEqualTo(2);
        rows.Should().OnlyContain(r => r.QuietHoursStart == new TimeOnly(21, 30));
    }

    // ── Tenant / user isolation (AC-5/BR-4) ─────────────────────────────────

    [Fact]
    public async Task GetMatrix_DoesNotLeakOtherTenantsPreferences()
    {
        // Same user id, different tenant — written via that tenant's context so only the global query filter
        // keeps it out.
        var otherTenantId = Guid.NewGuid();
        var otherTenantContext = Substitute.For<ITenantContext>();
        otherTenantContext.TenantId.Returns(otherTenantId);
        otherTenantContext.IsResolved.Returns(true);

        using (var otherDb = TestDbContextFactory.Create(otherTenantContext, _dbName))
        {
            otherDb.NotificationPreferences.Add(new NotificationPreference
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = otherTenantId,
                UserId = _userId,
                Category = NotificationCategory.LeaveUpdates,
                ChannelInApp = false,
                ChannelEmail = false, // would be an illegal state in OUR tenant
            });
            otherDb.SaveChanges();
        }

        // Our tenant has no saved prefs → matrix should be all defaults (LeaveUpdates email = true).
        var result = await CreateService().GetMatrixAsync();

        var leave = result.Value!.Categories.Single(c => c.Category == nameof(NotificationCategory.LeaveUpdates));
        leave.ChannelEmail.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateCategory_OnlyAffectsCallersOwnRows()
    {
        var otherUser = Guid.NewGuid();
        SeedPreference(new NotificationPreference
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            UserId = otherUser,
            Category = NotificationCategory.LeaveUpdates,
            ChannelInApp = true,
            ChannelEmail = true,
        });

        await CreateService().UpdateCategoryAsync(NotificationCategory.LeaveUpdates, true, false);

        using var db = CreateDbContext();
        var otherRow = db.NotificationPreferences.Single(p => p.UserId == otherUser);
        otherRow.ChannelEmail.Should().BeTrue(); // untouched
    }
}

/// <summary>
/// Minimal controllable <see cref="TimeProvider"/> for deterministic quiet-hours tests (no Microsoft.Extensions
/// .TimeProvider.Testing dependency in this test project). Returns a fixed UTC instant.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;
    public FakeTimeProvider(DateTimeOffset now) => _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
}
