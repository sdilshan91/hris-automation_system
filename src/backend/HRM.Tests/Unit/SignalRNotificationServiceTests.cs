// ============================================================================
// US-NTF-001: SignalRNotificationService (persist-then-push dispatcher) unit tests.
// Covers: the notification row is persisted with the EXPLICIT tenant id (works outside a
// resolved tenant context), the real-time push targets the recipient's user group
// (t:{tenantId}:user:{userId}) via the "ReceiveNotification" client method, and a SignalR
// push failure does NOT undo the persisted row (BR-4). Also checks the hub group-name convention.
// ============================================================================

using FluentAssertions;
using HRM.Api.Hubs;
using HRM.Api.Notifications;
using HRM.Infrastructure.Persistence;
using HRM.Tests.Unit.Helpers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class SignalRNotificationServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    // A scope factory whose AppDbContext shares the InMemory database name so the test can read back rows.
    private IServiceScopeFactory CreateScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => TestDbContextFactory.Create(_tenantId, _dbName));
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private AppDbContext ReadContext() => TestDbContextFactory.Create(_tenantId, _dbName);

    [Fact]
    public void HubGroupNames_FollowTenantScopedConvention()
    {
        NotificationHub.UserGroup(_tenantId, _userId).Should().Be($"t:{_tenantId}:user:{_userId}");
        NotificationHub.RoleGroup(_tenantId, "HR Manager").Should().Be($"t:{_tenantId}:role:HR Manager");
    }

    [Fact]
    public async Task CreateAndDispatch_PersistsRow_AndPushesToUserGroup()
    {
        var clientProxy = Substitute.For<IClientProxy>();
        var clients = Substitute.For<IHubClients>();
        clients.Group(Arg.Any<string>()).Returns(clientProxy);
        var hubContext = Substitute.For<IHubContext<NotificationHub>>();
        hubContext.Clients.Returns(clients);

        var service = new SignalRNotificationService(
            CreateScopeFactory(), hubContext, NullLogger<SignalRNotificationService>.Instance);

        var id = await service.CreateAndDispatchAsync(
            _tenantId, _userId, "leave_approved", "Leave approved", "Your leave was approved",
            resourceType: "LeaveRequest", resourceId: "abc-123");

        // Persisted with explicit tenant + recipient.
        using var db = ReadContext();
        var stored = db.Notifications.IgnoreQueryFilters().Single(n => n.Id == id);
        stored.TenantId.Should().Be(_tenantId);
        stored.UserId.Should().Be(_userId);
        stored.Type.Should().Be("leave_approved");
        stored.IsRead.Should().BeFalse();
        stored.ResourceType.Should().Be("LeaveRequest");
        stored.ResourceId.Should().Be("abc-123");

        // Pushed to the recipient's tenant-scoped user group via ReceiveNotification.
        clients.Received(1).Group(NotificationHub.UserGroup(_tenantId, _userId));
        await clientProxy.Received(1).SendCoreAsync(
            NotificationHub.ReceiveNotification,
            Arg.Is<object?[]>(a => a.Length == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAndDispatch_StillPersists_WhenSignalRPushThrows()
    {
        var clientProxy = Substitute.For<IClientProxy>();
        clientProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("backplane down"));
        var clients = Substitute.For<IHubClients>();
        clients.Group(Arg.Any<string>()).Returns(clientProxy);
        var hubContext = Substitute.For<IHubContext<NotificationHub>>();
        hubContext.Clients.Returns(clients);

        var service = new SignalRNotificationService(
            CreateScopeFactory(), hubContext, NullLogger<SignalRNotificationService>.Instance);

        var id = await service.CreateAndDispatchAsync(
            _tenantId, _userId, "task_assigned", "Task", "A task was assigned");

        // BR-4: the durable record survives even though the push failed.
        using var db = ReadContext();
        db.Notifications.IgnoreQueryFilters().Should().ContainSingle(n => n.Id == id);
    }
}
