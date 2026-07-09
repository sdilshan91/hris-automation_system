// ============================================================================
// US-NTF-006 (delivery Phase 1) — RealNotificationDispatcher email + in-app legs.
// The core dispatch logic: resolve the recipient email, apply the preference gate
// (with the BR-1 security bypass), render, write a NotificationDelivery row in the
// right terminal/queued state, and enqueue/schedule (or NOT) the Hangfire SendEmailJob.
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
//        #5 defer → Deferred row + Schedule     #6 security type bypasses the gate + Enqueue
//        #7 missing email / unknown template → Failed row + no throw
//        #8 in-app leg delegates to INotificationService
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

    // ── #3 Happy path: deliver → Queued row (correct email/eventKey) + Enqueue ──────────────
    [Fact]
    public async Task SendEmail_WhenDeliver_WritesQueuedRow_AndEnqueuesSendJob()
    {
        SeedTenant();
        SeedUserWithEmail("jane@acme.test");
        PrefsReturn(DeliveryDecision.Deliver());
        TemplateResolves();
        RenderReturns();

        await CreateDispatcher().SendEmailAsync(_tenantId, _userId, "leave.approved", "{}");

        var row = SingleDelivery();
        row.Should().NotBeNull();
        row!.Status.Should().Be(NotificationDeliveryStatus.Queued);
        row.Channel.Should().Be(NotificationDeliveryChannel.Email);
        row.RecipientEmail.Should().Be("jane@acme.test");
        row.RecipientUserId.Should().Be(_userId);
        row.NotificationType.Should().Be("leave.approved");
        row.EventKey.Should().Be("leave_approved"); // MapType("leave.*") fallback catalog key
        row.TenantId.Should().Be(_tenantId);

        AssertEnqueuedOnce();
    }

    // ── #4 Suppressed (non-mandatory) → Suppressed row + NO enqueue ─────────────────────────
    [Fact]
    public async Task SendEmail_WhenSuppressed_WritesSuppressedRow_AndDoesNotEnqueue()
    {
        SeedTenant();
        SeedUserWithEmail("jane@acme.test");
        PrefsReturn(DeliveryDecision.Suppressed());

        await CreateDispatcher().SendEmailAsync(_tenantId, _userId, "leave.approved", "{}");

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

        await CreateDispatcher().SendEmailAsync(_tenantId, _userId, "leave.approved", "{}");

        SingleDelivery()!.Status.Should().Be(NotificationDeliveryStatus.Deferred);
        AssertScheduledOnce();
        // Explicitly: it was scheduled, NOT immediate-enqueued.
        _jobs.DidNotReceive().Create(Arg.Any<Job>(), Arg.Is<IState>(s => s is EnqueuedState));
    }

    // ── #6 Non-suppressible security type bypasses the gate (BR-1) → always enqueues ────────
    [Fact]
    public async Task SendEmail_SecurityType_BypassesPreferenceGate_AndAlwaysEnqueues()
    {
        SeedTenant();
        SeedUserWithEmail("jane@acme.test");
        // Even though the gate WOULD suppress, a security type must never consult it.
        PrefsReturn(DeliveryDecision.Suppressed());
        TemplateResolves();
        RenderReturns();

        await CreateDispatcher().SendEmailAsync(_tenantId, _userId, "password.reset", "{}");

        SingleDelivery()!.Status.Should().Be(NotificationDeliveryStatus.Queued);
        AssertEnqueuedOnce();

        // Gate bypass proof: the preference service was never asked.
        await _prefs.DidNotReceive().ShouldDeliverAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<NotificationCategory>(),
            Arg.Any<NotificationChannel>(), Arg.Any<CancellationToken>());
    }

    // ── #7a Missing recipient email → Failed row, no throw, no enqueue ──────────────────────
    [Fact]
    public async Task SendEmail_WhenNoRecipientEmail_WritesFailedRow_AndDoesNotThrow()
    {
        SeedTenant();
        // No user row → email cannot be resolved.
        PrefsReturn(DeliveryDecision.Deliver());

        var act = async () => await CreateDispatcher().SendEmailAsync(_tenantId, _userId, "leave.approved", "{}");

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

        var act = async () => await CreateDispatcher().SendEmailAsync(_tenantId, _userId, "leave.approved", "{}");

        await act.Should().NotThrowAsync();
        var row = SingleDelivery();
        row!.Status.Should().Be(NotificationDeliveryStatus.Failed);
        row.LastError.Should().Contain("template");
        AssertNothingEnqueued();
    }

    // ── #8 In-app leg delegates to INotificationService with mapped title/message ────────────
    [Fact]
    public async Task SendInApp_DelegatesToNotificationService_WithMappedTitleAndMessage()
    {
        var payload = """{"title":"Leave approved","message":"Your leave was approved","resourceType":"LeaveRequest","resourceId":"lr-1"}""";

        await CreateDispatcher().SendInAppAsync(_tenantId, _userId, "leave.approved", payload);

        await _inApp.Received(1).CreateAndDispatchAsync(
            _tenantId, _userId, "leave.approved",
            "Leave approved", "Your leave was approved",
            "LeaveRequest", "lr-1",
            Arg.Any<CancellationToken>());
    }
}
