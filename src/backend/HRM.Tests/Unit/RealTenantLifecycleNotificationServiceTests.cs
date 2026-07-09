// ============================================================================
// US-NTF-006 Phase 2a — RealTenantLifecycleNotificationService (US-ADM-004).
// Fans a lifecycle event (suspended / termination-initiated / reactivated /
// restored) out to the tenant's billing email + admin emails as RAW addresses
// (RecipientEmail override, no User row required), mapping the lowercase event
// type to the right catalog EventKey, and never throwing on a dispatch failure.
//
// Provider: EF Core InMemory AppDbContext (no seeded users → all recipients are
// email-only). INotificationDispatcher is substituted (NSubstitute).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class RealTenantLifecycleNotificationServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly INotificationDispatcher _dispatcher = Substitute.For<INotificationDispatcher>();

    private RealTenantLifecycleNotificationService CreateService() =>
        new(TestDbContextFactory.Create(_tenantId), _dispatcher,
            NullLogger<RealTenantLifecycleNotificationService>.Instance);

    private TenantLifecycleNotification Notification(
        string eventType, string? billingEmail, params string[] adminEmails) =>
        new(
            TenantId: _tenantId,
            TenantName: "Acme Corporation",
            TenantSubdomain: "acme",
            EventType: eventType,
            Reason: "Overdue invoice",
            TerminationScheduledAt: null,
            BillingEmail: billingEmail,
            AdminEmails: adminEmails);

    // ── Suspended: email to billing + all admin raw addresses, mapped to tenant_suspended ───
    [Fact]
    public async Task NotifyLifecycleChange_Suspended_DispatchesEmailToAllRawAddresses_WithCorrectEventKey()
    {
        var notification = Notification(
            "suspended", billingEmail: "billing@acme.test", "admin1@acme.test", "admin2@acme.test");

        await CreateService().NotifyLifecycleChangeAsync(notification);

        foreach (var address in new[] { "billing@acme.test", "admin1@acme.test", "admin2@acme.test" })
        {
            await _dispatcher.Received(1).SendEmailAsync(
                Arg.Is<NotificationRequest>(r =>
                    r.RecipientEmail == address && r.EventKey == "tenant_suspended" &&
                    r.TenantId == _tenantId && r.RecipientUserId == null),
                Arg.Any<CancellationToken>());
        }

        // Exactly the three raw recipients, and — with no matching users — no in-app leg.
        await _dispatcher.Received(3).SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().SendInAppAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Event-type → catalog EventKey mapping (termination_initiated) ───────────────────────
    [Fact]
    public async Task NotifyLifecycleChange_TerminationInitiated_MapsToTerminationEventKey()
    {
        var notification = Notification(
            "termination_initiated", billingEmail: "billing@acme.test");

        await CreateService().NotifyLifecycleChangeAsync(notification);

        await _dispatcher.Received(1).SendEmailAsync(
            Arg.Is<NotificationRequest>(r =>
                r.RecipientEmail == "billing@acme.test" && r.EventKey == "tenant_termination_initiated"),
            Arg.Any<CancellationToken>());
    }

    // ── Dispatcher failure never propagates (lifecycle transition already committed) ────────
    [Fact]
    public async Task NotifyLifecycleChange_WhenDispatcherThrows_DoesNotThrow()
    {
        _dispatcher
            .When(d => d.SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("smtp down"));

        var act = async () => await CreateService().NotifyLifecycleChangeAsync(
            Notification("suspended", billingEmail: "billing@acme.test"));

        await act.Should().NotThrowAsync();
    }
}
