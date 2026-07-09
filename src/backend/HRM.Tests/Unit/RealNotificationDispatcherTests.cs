// ============================================================================
// US-NTF-006 (delivery Phase 1 + Phase 2a contract) — RealNotificationDispatcher
// email + in-app legs, expressed via the new single-record NotificationRequest API.
// The core dispatch logic: resolve the recipient email (RecipientEmail override OR
// Users.Email by RecipientUserId), apply the preference gate (with the BR-1 security
// bypass — category + mandatory now come from NotificationEventCatalog.Get(EventKey)),
// render, write a NotificationDelivery row in the right terminal/queued state, and
// enqueue/schedule (or NOT) the Hangfire SendEmailJob.
//
// Provider: EF Core InMemory AppDbContext wired through a real IServiceScopeFactory
// (the dispatcher opens its own scope + restores tenant context), mirroring
// SignalRNotificationServiceTests. Collaborators faked with NSubstitute:
//   • INotificationPreferenceService  → controls the ShouldDeliver decision
//   • IEmailTemplateService           → controls resolve success/failure + render
//   • IBackgroundJobClient            → observes enqueue vs schedule vs neither
//                                       (Enqueue<T>/Schedule<T> lower to Create(job,state))
//   • INotificationService            → the in-app leg delegate
//
// Maps:  #3 deliver → Queued row + Enqueue     #4 suppressed → Suppressed row + NO enqueue
//        #5 defer → Deferred row + Schedule     #6 mandatory/security bypasses the gate + Enqueue
//        #7 missing email / unknown template → Failed row + no throw
//        #8 in-app delegates to INotificationService   #8b in-app skipped when no user
//        #9 RecipientEmail override (raw address, no user) → Queued (userId=Guid.Empty), gate NOT consulted
//       #10 category comes from the catalog (LeaveUpdates gate call), mandatory from the catalog (bypass)
// ============================================================================

using FluentAssertions;
using HRM.Api.Jobs;
using HRM.Api.Notifications;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class RealNotificationDispatcherTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly INotificationPreferenceService _prefs = Substitute.For<INotificationPreferenceService>();
    private readonly IEmailTemplateService _templates = Substitute.For<IEmailTemplateService>();
    private readonly INotificationService _inApp = Substitute.For<INotificationService>();
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();

    // Real scope factory whose AppDbContext + TenantContext come from the SAME scope (so the dispatcher's
    // SetTenant on the scope's ITenantContext is the one its AppDbContext query filters observe), sharing the
    // named InMemory store the test seeds/reads.
    private IServiceScopeFactory BuildScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped(sp =>
        {
            var tc = sp.GetRequiredService<ITenantContext>();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
            return new AppDbContext(options, tc);
        });
        services.AddSingleton(_prefs);
        services.AddSingleton(_templates);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private RealNotificationDispatcher CreateDispatcher() =>
        new(BuildScopeFactory(), _inApp, _jobs, NullLogger<RealNotificationDispatcher>.Instance);

    private AppDbContext ReadContext() => TestDbContextFactory.Create(_tenantId, _dbName);

    private void SeedTenant() =>
        Seed(db => db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Status = TenantStatus.Active }));

    private void SeedUserWithEmail(string email) =>
        Seed(db => db.Users.Add(new User { Id = _userId, Email = email }));

    private void Seed(Action<AppDbContext> seed)
    {
        using var db = TestDbContextFactory.Create(_tenantId, _dbName);
        seed(db);
        db.SaveChanges();
    }

    private NotificationDelivery? SingleDelivery()
    {
        using var db = ReadContext();
        return db.NotificationDeliveries.SingleOrDefault();
    }

    // Build a request carrying the catalog EventKey directly (category + mandatory are looked up from the
    // catalog by the dispatcher — no more MapType heuristic).
    private NotificationRequest Request(
        string eventKey, Guid? recipientUserId = null, string? recipientEmail = null) =>
        new(_tenantId, eventKey, "{}", RecipientUserId: recipientUserId, RecipientEmail: recipientEmail);

    private void PrefsReturn(DeliveryDecision decision) =>
        _prefs.ShouldDeliverAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<NotificationCategory>(),
            Arg.Any<NotificationChannel>(), Arg.Any<CancellationToken>()).Returns(decision);

    private void TemplateResolves() =>
        _templates.ResolveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Application.Common.Models.Result<ResolvedEmailTemplate>.Success(
                new ResolvedEmailTemplate("leave_approved", "en", "Subj", "<p>h</p>", "t", false, 1, null))));

    private void RenderReturns() =>
        _templates.Render(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object?>>())
            .Returns(new RenderedEmail("Rendered subject", "<p>rendered</p>", "rendered"));

    // NSubstitute assertions on the lowered Create(job, state) call — Enqueue<T>/Schedule<T> are extension
    // methods that translate to IBackgroundJobClient.Create with an EnqueuedState / ScheduledState respectively.
    private void AssertEnqueuedOnce() =>
        _jobs.Received(1).Create(
            Arg.Is<Job>(j => j.Method.Name == nameof(SendEmailJob.RunAsync)),
            Arg.Is<IState>(s => s is EnqueuedState));

    private void AssertScheduledOnce() =>
        _jobs.Received(1).Create(
            Arg.Is<Job>(j => j.Method.Name == nameof(SendEmailJob.RunAsync)),
            Arg.Is<IState>(s => s is ScheduledState));

    private void AssertNothingEnqueued() =>
        _jobs.DidNotReceive().Create(Arg.Any<Job>(), Arg.Any<IState>());

    private Task AssertGateNotConsulted() =>
        _prefs.DidNotReceive().ShouldDeliverAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<NotificationCategory>(),
            Arg.Any<NotificationChannel>(), Arg.Any<CancellationToken>());

    // ── #3 Happy path: deliver → Queued row (correct email/eventKey) + Enqueue ──────────────
    [Fact]
    public async Task SendEmail_WhenDeliver_WritesQueuedRow_AndEnqueuesSendJob()
    {
        SeedTenant();
        SeedUserWithEmail("jane@acme.test");
        PrefsReturn(DeliveryDecision.Deliver());
        TemplateResolves();
        RenderReturns();

        await CreateDispatcher().SendEmailAsync(Request("leave_approved", recipientUserId: _userId));

        var row = SingleDelivery();
        row.Should().NotBeNull();
        row!.Status.Should().Be(NotificationDeliveryStatus.Queued);
        row.Channel.Should().Be(NotificationDeliveryChannel.Email);
        row.RecipientEmail.Should().Be("jane@acme.test");
        row.RecipientUserId.Should().Be(_userId);
        row.NotificationType.Should().Be("leave_approved"); // defaults to EventKey when NotificationType is null
        row.EventKey.Should().Be("leave_approved");
        row.TenantId.Should().Be(_tenantId);

        AssertEnqueuedOnce();
    }

    // ── #10 Category is read from the catalog: leave_approved → LeaveUpdates gate call ──────
    [Fact]
    public async Task SendEmail_NonMandatoryEvent_ConsultsGate_WithCatalogCategory()
    {
        SeedTenant();
        SeedUserWithEmail("jane@acme.test");
        PrefsReturn(DeliveryDecision.Deliver());
        TemplateResolves();
        RenderReturns();

        await CreateDispatcher().SendEmailAsync(Request("leave_approved", recipientUserId: _userId));

        // The catalog says leave_approved is category LeaveUpdates + not mandatory → the gate is consulted
        // with exactly that category (proving category comes from NotificationEventCatalog, not a guess).
        await _prefs.Received(1).ShouldDeliverAsync(
            _tenantId, _userId, NotificationCategory.LeaveUpdates,
            NotificationChannel.Email, Arg.Any<CancellationToken>());
    }

    // ── #4 Suppressed (non-mandatory) → Suppressed row + NO enqueue ─────────────────────────
    [Fact]
    public async Task SendEmail_WhenSuppressed_WritesSuppressedRow_AndDoesNotEnqueue()
    {
        SeedTenant();
        SeedUserWithEmail("jane@acme.test");
        PrefsReturn(DeliveryDecision.Suppressed());

        await CreateDispatcher().SendEmailAsync(Request("leave_approved", recipientUserId: _userId));

        SingleDelivery()!.Status.Should().Be(NotificationDeliveryStatus.Suppressed);
        AssertNothingEnqueued();

        // The template is never resolved/rendered on the suppressed path.
        await _templates.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    // ── #5 Defer → Deferred row + SCHEDULE (not immediate enqueue) ──────────────────────────
    [Fact]
    public async Task SendEmail_WhenDeferred_WritesDeferredRow_AndSchedulesInsteadOfEnqueue()
    {
        SeedTenant();
        SeedUserWithEmail("jane@acme.test");
        PrefsReturn(DeliveryDecision.Defer(DateTime.UtcNow.AddHours(6)));
        TemplateResolves();
        RenderReturns();

        await CreateDispatcher().SendEmailAsync(Request("leave_approved", recipientUserId: _userId));

        SingleDelivery()!.Status.Should().Be(NotificationDeliveryStatus.Deferred);
        AssertScheduledOnce();
        // Explicitly: it was scheduled, NOT immediate-enqueued.
        _jobs.DidNotReceive().Create(Arg.Any<Job>(), Arg.Is<IState>(s => s is EnqueuedState));
    }

    // ── #6 Mandatory/security event bypasses the gate (BR-1, from the catalog) → always enqueues ──
    // Both password_reset (SecurityAlerts) and impersonation_started (SecurityAlerts) are IsMandatory in
    // the catalog: even when the gate WOULD suppress, the dispatcher must never consult it and must enqueue.
    [Theory]
    [InlineData("password_reset")]
    [InlineData("impersonation_started")]
    public async Task SendEmail_MandatoryEvent_BypassesPreferenceGate_AndAlwaysEnqueues(string eventKey)
    {
        SeedTenant();
        SeedUserWithEmail("jane@acme.test");
        // Even though the gate WOULD suppress, a mandatory event must never consult it.
        PrefsReturn(DeliveryDecision.Suppressed());
        TemplateResolves();
        RenderReturns();

        await CreateDispatcher().SendEmailAsync(Request(eventKey, recipientUserId: _userId));

        SingleDelivery()!.Status.Should().Be(NotificationDeliveryStatus.Queued);
        AssertEnqueuedOnce();

        // Gate-bypass proof: the preference service was never asked (mandatory flag comes from the catalog).
        await AssertGateNotConsulted();
    }

    // ── #7a Missing recipient email → Failed row, no throw, no enqueue ──────────────────────
    [Fact]
    public async Task SendEmail_WhenNoRecipientEmail_WritesFailedRow_AndDoesNotThrow()
    {
        SeedTenant();
        // No user row and no RecipientEmail → email cannot be resolved.
        PrefsReturn(DeliveryDecision.Deliver());

        var act = async () => await CreateDispatcher().SendEmailAsync(Request("leave_approved", recipientUserId: _userId));

        await act.Should().NotThrowAsync();
        var row = SingleDelivery();
        row!.Status.Should().Be(NotificationDeliveryStatus.Failed);
        row.LastError.Should().NotBeNullOrEmpty();
        AssertNothingEnqueued();
    }

    // ── #7b Unknown template → Failed row, no throw, no enqueue ─────────────────────────────
    [Fact]
    public async Task SendEmail_WhenTemplateResolutionFails_WritesFailedRow_AndDoesNotThrow()
    {
        SeedTenant();
        SeedUserWithEmail("jane@acme.test");
        PrefsReturn(DeliveryDecision.Deliver());
        _templates.ResolveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Application.Common.Models.Result<ResolvedEmailTemplate>.Failure(
                "No template", statusCode: 404, errorCode: "not_found")));

        var act = async () => await CreateDispatcher().SendEmailAsync(Request("leave_approved", recipientUserId: _userId));

        await act.Should().NotThrowAsync();
        var row = SingleDelivery();
        row!.Status.Should().Be(NotificationDeliveryStatus.Failed);
        row.LastError.Should().Contain("template");
        AssertNothingEnqueued();
    }

    // ── #9 RecipientEmail override (raw address, no user) → Queued (userId=Guid.Empty) + Enqueue, gate NOT consulted ──
    [Fact]
    public async Task SendEmail_WithRawRecipientEmail_NoUser_WritesQueuedRowToRawAddress_AndBypassesGate()
    {
        SeedTenant();
        // No user seeded — the raw RecipientEmail must be used directly, and the per-user gate must NOT run.
        PrefsReturn(DeliveryDecision.Suppressed()); // even if it would suppress, it should never be consulted (no user)
        TemplateResolves();
        RenderReturns();

        await CreateDispatcher().SendEmailAsync(
            Request("leave_approved", recipientUserId: null, recipientEmail: "billing@acme.test"));

        var row = SingleDelivery();
        row!.Status.Should().Be(NotificationDeliveryStatus.Queued);
        row.RecipientEmail.Should().Be("billing@acme.test");
        row.RecipientUserId.Should().Be(Guid.Empty); // raw-email (non-provisioned) recipient
        row.EventKey.Should().Be("leave_approved");
        AssertEnqueuedOnce();

        // The per-user preference gate is only evaluated when a RecipientUserId exists.
        await AssertGateNotConsulted();
    }

    // ── #8 In-app leg delegates to INotificationService with mapped title/message ────────────
    [Fact]
    public async Task SendInApp_DelegatesToNotificationService_WithMappedTitleAndMessage()
    {
        var payload = """{"title":"Leave approved","message":"Your leave was approved","resourceType":"LeaveRequest","resourceId":"lr-1"}""";

        await CreateDispatcher().SendInAppAsync(
            new NotificationRequest(_tenantId, "leave_approved", payload, RecipientUserId: _userId));

        await _inApp.Received(1).CreateAndDispatchAsync(
            _tenantId, _userId, "leave_approved",
            "Leave approved", "Your leave was approved",
            "LeaveRequest", "lr-1",
            Arg.Any<CancellationToken>());
    }

    // ── #8b In-app leg is skipped when there is no RecipientUserId (cannot push in-app to a non-user) ──
    [Fact]
    public async Task SendInApp_WhenNoRecipientUserId_IsSkipped()
    {
        await CreateDispatcher().SendInAppAsync(
            new NotificationRequest(_tenantId, "tenant_suspended", "{}",
                RecipientUserId: null, RecipientEmail: "billing@acme.test"));

        await _inApp.DidNotReceive().CreateAndDispatchAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
