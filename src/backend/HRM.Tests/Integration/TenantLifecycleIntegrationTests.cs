// ============================================================================
// US-ADM-004: tenant lifecycle service integration tests (InMemory, system context).
//
//   - AC-1: Suspend → Suspended + SuspendedAt/Reason, all refresh tokens revoked (BR-5), "suspended" lifecycle
//           event + "Tenant.Suspended" audit, notification dispatched.
//   - AC-3: Terminate → Terminating + TerminationScheduledAt = now+grace, "termination_initiated" event.
//   - AC-5: Reactivate Suspended → Active, suspension fields cleared.
//   - AC-6: Restore Terminating → prior state (Suspended if it was suspended before, else Active), scheduled-at
//           cleared. BR-3: a Terminated tenant cannot be restored.
//   - BR-1: invalid transitions rejected (suspend an already-suspended tenant). BR-2: system tenant protected.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Tenants.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class TenantLifecycleIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _adminId = Guid.NewGuid();

    private sealed class SystemTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public string Subdomain => "admin";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => true;
        public bool IsResolved => false;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }

    private AppDbContext Db()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options,
            new SystemTenantContext());

    private (TenantLifecycleService Service, ITenantLifecycleNotificationService Notify) Service()
    {
        var notify = Substitute.For<ITenantLifecycleNotificationService>();
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(_adminId);
        user.IsAuthenticated.Returns(true);
        var config = new ConfigurationBuilder().Build(); // system tenant subdomain defaults to "platform"
        var service = new TenantLifecycleService(
            Db(), user, notify, config, NullLogger<TenantLifecycleService>.Instance, scheduler: null);
        return (service, notify);
    }

    private async Task<Guid> SeedTenantAsync(string subdomain, TenantStatus status,
        DateTime? suspendedAt = null, DateTime? terminationAt = null)
    {
        var id = BaseEntity.NewUuidV7();
        using var db = Db();
        db.Tenants.Add(new Tenant
        {
            Id = id, Subdomain = subdomain, Name = subdomain, Status = status,
            PlanId = "starter", BillingEmail = $"billing@{subdomain}.test",
            SuspendedAt = suspendedAt, SuspendedReason = suspendedAt is null ? null : "prior",
            TerminationScheduledAt = terminationAt, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SeedActiveRefreshTokenAsync(Guid tenantId)
    {
        using var db = Db();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), TenantId = tenantId,
            TokenHash = "hash", IssuedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        await db.SaveChangesAsync();
    }

    // ── AC-1: suspend ───────────────────────────────────────────────────────

    [Fact]
    public async Task Suspend_ActiveTenant_SetsStateRevokesTokensAndAudits()
    {
        var tenantId = await SeedTenantAsync("acme", TenantStatus.Active);
        await SeedActiveRefreshTokenAsync(tenantId);
        var (service, notify) = Service();

        var result = await service.SuspendAsync(new SuspendTenantInput(tenantId, "Repeated abuse reports filed."));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Status.Should().Be(nameof(TenantStatus.Suspended));

        using var db = Db();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenantId);
        tenant.Status.Should().Be(TenantStatus.Suspended);
        tenant.SuspendedAt.Should().NotBeNull();
        tenant.SuspendedReason.Should().Be("Repeated abuse reports filed.");

        (await db.RefreshTokens.IgnoreQueryFilters().Where(rt => rt.TenantId == tenantId).AllAsync(rt => rt.RevokedAt != null))
            .Should().BeTrue(); // BR-5

        (await db.TenantLifecycleEvents.IgnoreQueryFilters()
            .AnyAsync(e => e.TenantId == tenantId && e.EventType == "suspended")).Should().BeTrue();
        (await db.AuditLogs.IgnoreQueryFilters()
            .AnyAsync(a => a.TenantId == tenantId && a.Action == "Tenant.Suspended")).Should().BeTrue();

        await notify.Received(1).NotifyLifecycleChangeAsync(
            Arg.Is<TenantLifecycleNotification>(n => n.EventType == "suspended"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suspend_AlreadySuspended_IsRejected()
    {
        var tenantId = await SeedTenantAsync("acme", TenantStatus.Suspended, suspendedAt: DateTime.UtcNow);
        var (service, _) = Service();

        var result = await service.SuspendAsync(new SuspendTenantInput(tenantId, "A second suspension attempt."));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_transition");
    }

    // ── BR-2: system tenant protected ───────────────────────────────────────

    [Fact]
    public async Task Suspend_SystemTenant_IsRejected()
    {
        var tenantId = await SeedTenantAsync("platform", TenantStatus.Active);
        var (service, _) = Service();

        var result = await service.SuspendAsync(new SuspendTenantInput(tenantId, "Trying to suspend the platform."));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("system_tenant_protected");
    }

    // ── AC-3: terminate ─────────────────────────────────────────────────────

    [Fact]
    public async Task Terminate_ActiveTenant_SchedulesAndSetsTerminating()
    {
        var tenantId = await SeedTenantAsync("acme", TenantStatus.Active);
        var (service, _) = Service();

        var result = await service.TerminateAsync(new TerminateTenantInput(tenantId, "Non-payment after dunning.", 30));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Status.Should().Be(nameof(TenantStatus.Terminating));
        result.Value.TerminationScheduledAt.Should().NotBeNull();

        using var db = Db();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenantId);
        tenant.Status.Should().Be(TenantStatus.Terminating);
        tenant.TerminationScheduledAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
        (await db.TenantLifecycleEvents.IgnoreQueryFilters()
            .AnyAsync(e => e.TenantId == tenantId && e.EventType == "termination_initiated")).Should().BeTrue();
    }

    // ── AC-5: reactivate ────────────────────────────────────────────────────

    [Fact]
    public async Task Reactivate_SuspendedTenant_ReturnsToActive()
    {
        var tenantId = await SeedTenantAsync("acme", TenantStatus.Suspended, suspendedAt: DateTime.UtcNow);
        var (service, _) = Service();

        var result = await service.ReactivateAsync(new ReactivateTenantInput(tenantId));

        result.IsSuccess.Should().BeTrue(result.Error);
        using var db = Db();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenantId);
        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.SuspendedAt.Should().BeNull();
        tenant.SuspendedReason.Should().BeNull();
        (await db.TenantLifecycleEvents.IgnoreQueryFilters()
            .AnyAsync(e => e.TenantId == tenantId && e.EventType == "reactivated")).Should().BeTrue();
    }

    // ── AC-6: restore ───────────────────────────────────────────────────────

    [Fact]
    public async Task Restore_TerminatingFromActive_ReturnsToActive()
    {
        var tenantId = await SeedTenantAsync("acme", TenantStatus.Terminating, terminationAt: DateTime.UtcNow.AddDays(30));
        var (service, _) = Service();

        var result = await service.RestoreAsync(new RestoreTenantInput(tenantId));

        result.IsSuccess.Should().BeTrue(result.Error);
        using var db = Db();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenantId);
        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.TerminationScheduledAt.Should().BeNull();
        (await db.TenantLifecycleEvents.IgnoreQueryFilters()
            .AnyAsync(e => e.TenantId == tenantId && e.EventType == "restored")).Should().BeTrue();
    }

    [Fact]
    public async Task Restore_TerminatingThatWasSuspended_ReturnsToSuspended()
    {
        var tenantId = await SeedTenantAsync("acme", TenantStatus.Terminating,
            suspendedAt: DateTime.UtcNow.AddDays(-2), terminationAt: DateTime.UtcNow.AddDays(30));
        var (service, _) = Service();

        var result = await service.RestoreAsync(new RestoreTenantInput(tenantId));

        result.IsSuccess.Should().BeTrue(result.Error);
        (await Db().Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenantId)).Status
            .Should().Be(TenantStatus.Suspended);
    }

    [Fact]
    public async Task Restore_TerminatedTenant_IsRejected()
    {
        var tenantId = await SeedTenantAsync("acme", TenantStatus.Terminated);
        var (service, _) = Service();

        var result = await service.RestoreAsync(new RestoreTenantInput(tenantId));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_transition"); // BR-3
    }

    // ── lifecycle history ───────────────────────────────────────────────────

    [Fact]
    public async Task GetLifecycleHistory_ReturnsEventsNewestFirst()
    {
        var tenantId = await SeedTenantAsync("acme", TenantStatus.Active);
        var (service, _) = Service();
        await service.SuspendAsync(new SuspendTenantInput(tenantId, "First suspension event."));
        await service.ReactivateAsync(new ReactivateTenantInput(tenantId));

        var history = await service.GetLifecycleHistoryAsync(tenantId);

        history.IsSuccess.Should().BeTrue(history.Error);
        history.Value!.Select(e => e.EventType).Should().ContainInOrder("reactivated", "suspended");
    }
}
