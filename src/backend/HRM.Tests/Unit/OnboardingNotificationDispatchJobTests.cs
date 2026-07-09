// ============================================================================
// US-NTF-006 Phase 3 — OnboardingNotificationDispatchJob outbox-type → EventKey mapping.
//
// MapOutboxTypeToEventKey (private) maps the outbox's free-form NotificationType to a
// NotificationEventCatalog event key:
//   onboarding.checklist.assigned → onboarding_checklist_assigned
//   onboarding.task.completed     → onboarding_task_completed
//   onboarding.task.overdue       → onboarding_task_overdue
//   (anything else)               → onboarding_welcome   (fallback)
//
// The map is private+static, so we drive the JOB with a seeded Pending outbox row and
// assert the EventKey on the NotificationRequest the job hands to INotificationDispatcher
// (both SendInAppAsync + SendEmailAsync). The job resolves AppDbContext + the dispatcher
// from its own DI scope (IServiceScopeFactory), mirroring RealNotificationDispatcherTests.
// A successful dispatch also flips the row to Dispatched.
// ============================================================================

using FluentAssertions;
using HRM.Api.Jobs;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class OnboardingNotificationDispatchJobTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly INotificationDispatcher _dispatcher = Substitute.For<INotificationDispatcher>();

    // A real scope factory: the job opens a scope, resolves AppDbContext (sharing the named InMemory store the
    // test seeds) and INotificationDispatcher (our mock). Tenant context value is immaterial — the job reads the
    // outbox via IgnoreQueryFilters() scoped by the explicit tenant id.
    private IServiceScopeFactory BuildScopeFactory()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(_tenantId);
        tenantContext.IsResolved.Returns(true);

        var services = new ServiceCollection();
        services.AddSingleton(tenantContext);
        services.AddScoped(sp =>
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
            return new AppDbContext(options, sp.GetRequiredService<ITenantContext>());
        });
        services.AddSingleton(_dispatcher);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private Guid SeedOutbox(string notificationType)
    {
        var id = Guid.NewGuid();
        using var db = TestDbContextFactory.Create(_tenantId, _dbName);
        db.OnboardingNotificationOutbox.Add(new OnboardingNotificationOutbox
        {
            Id = id,
            TenantId = _tenantId,
            ChecklistInstanceId = Guid.NewGuid(),
            RecipientUserId = Guid.NewGuid(),
            RecipientRole = OnboardingResponsibleRole.Employee,
            NotificationType = notificationType,
            Payload = "{}",
            Status = OnboardingNotificationStatus.Pending,
        });
        db.SaveChanges();
        return id;
    }

    private OnboardingNotificationOutbox ReadRow(Guid id)
    {
        using var db = TestDbContextFactory.Create(_tenantId, _dbName);
        return db.OnboardingNotificationOutbox.IgnoreQueryFilters().Single(o => o.Id == id);
    }

    private List<NotificationRequest> CaptureEmails()
    {
        var captured = new List<NotificationRequest>();
        _dispatcher.SendEmailAsync(Arg.Do<NotificationRequest>(captured.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return captured;
    }

    [Theory]
    [InlineData("onboarding.checklist.assigned", "onboarding_checklist_assigned")]
    [InlineData("onboarding.task.completed", "onboarding_task_completed")]
    [InlineData("onboarding.task.overdue", "onboarding_task_overdue")]
    [InlineData("onboarding.something.unmapped", "onboarding_welcome")] // fallback
    [InlineData("", "onboarding_welcome")]                              // fallback
    public async Task RunAsync_MapsOutboxType_ToExpectedEventKey_AndDispatches(
        string notificationType, string expectedEventKey)
    {
        var rowId = SeedOutbox(notificationType);
        var captured = CaptureEmails();

        await new OnboardingNotificationDispatchJob(BuildScopeFactory()).RunAsync(_tenantId);

        // Both legs receive the request with the mapped EventKey.
        await _dispatcher.Received(1).SendInAppAsync(
            Arg.Is<NotificationRequest>(r => r.EventKey == expectedEventKey), Arg.Any<CancellationToken>());
        await _dispatcher.Received(1).SendEmailAsync(
            Arg.Is<NotificationRequest>(r => r.EventKey == expectedEventKey), Arg.Any<CancellationToken>());

        captured.Should().ContainSingle().Which.EventKey.Should().Be(expectedEventKey);
        // The verbatim outbox type is preserved on the request for the delivery-audit row.
        captured[0].NotificationType.Should().Be(notificationType);

        // A clean dispatch flips the row to Dispatched.
        ReadRow(rowId).Status.Should().Be(OnboardingNotificationStatus.Dispatched);
    }

    [Fact]
    public async Task RunAsync_WithNoPendingRows_DispatchesNothing()
    {
        await new OnboardingNotificationDispatchJob(BuildScopeFactory()).RunAsync(_tenantId);

        await _dispatcher.DidNotReceiveWithAnyArgs().SendInAppAsync(default!, default);
        await _dispatcher.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default);
    }
}
