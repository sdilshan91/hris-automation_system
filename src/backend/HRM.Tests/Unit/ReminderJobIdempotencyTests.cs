// ============================================================================
// ISSUE-116 (US-REC-005 NFR-4 / US-REC-007 FR-7) — reminder jobs must be idempotent
// across a Hangfire RE-EXECUTION.
//
// NFR-4: "a re-execution (Hangfire retry) does not send duplicate reminders."
// Both reminder jobs violated it:
//
//   * InterviewReminderJob guarded only on `interview is null || Status != Scheduled`.
//     A retry for a still-Scheduled interview dispatched a SECOND reminder.
//   * OfferExpiryReminderJob guarded only on `offer is null || !offer.IsActive`. It
//     CLEARED `ExpiryReminderJobId` but never READ it, so a retry for a still-active
//     offer dispatched a SECOND reminder.
//
// The fix reuses the reminder-job-id markers that ALREADY exist and are already
// persisted on every scheduling path (Interview.ReminderJobId — set in
// InterviewService.ScheduleAsync/RescheduleAsync; Offer.ExpiryReminderJobId — set in
// OfferService.SendAsync). No new column, no migration.
//
// These tests drive the PUBLIC RunAsync TWICE against a real DI scope (the job opens
// its own scope per run, so run 2 genuinely re-reads committed state) and assert the
// notification seam was invoked EXACTLY ONCE. Received(1) is a two-sided assertion:
// 0 calls (job broken / never dispatches) fails just as loudly as 2 (the defect).
//
// InMemory is deliberate and matches the sibling job tests
// (LeaveYearEndJobRetryTests, OnboardingNotificationDispatchJobTests): nothing here
// depends on Postgres SQL semantics — the behaviour under test is "clear a marker,
// commit it, and re-read it in a fresh scope."
// ============================================================================

using FluentAssertions;
using HRM.Api.Jobs;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class ReminderJobIdempotencyTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    // BUG-530 changed the seam from Task to Task<Result>. A bare Substitute returns null for
    // Task<Result>, so these ISSUE-116 tests must stub a SUCCEEDING seam — otherwise every job
    // under test fails on the null Result before it ever reaches the idempotency guard being asserted.
    private readonly IRecruitmentNotificationService _notifications =
        HRM.Tests.Unit.Helpers.RecruitmentNotifications.Succeeding();

    // ── Interview reminder ──────────────────────────────────────────────────

    [Fact]
    [Trait("TC", "TC-REC-005-02")]
    public async Task InterviewReminderJob_ReExecution_SendsExactlyOneReminder_ISSUE116()
    {
        var interviewId = SeedScheduledInterviewWithPendingReminder();
        var job = new InterviewReminderJob(BuildScopeFactory());

        // Run 1 = the scheduled execution. Run 2 = the Hangfire retry (identical job args).
        await job.RunAsync(_tenantId, interviewId);
        await job.RunAsync(_tenantId, interviewId);

        await _notifications.Received(1).NotifyInterviewReminderAsync(
            interviewId,
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<Guid>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("TC", "TC-REC-005-02")]
    public async Task InterviewReminderJob_ClearsReminderMarker_ISSUE116()
    {
        var interviewId = SeedScheduledInterviewWithPendingReminder();
        var job = new InterviewReminderJob(BuildScopeFactory());

        await job.RunAsync(_tenantId, interviewId);

        using var db = TestDbContextFactory.Create(_tenantId, _dbName);
        var stored = db.Interviews.Single(i => i.Id == interviewId);
        stored.ReminderJobId.Should().BeNull(
            "the dispatched reminder must be CLAIMED (marker cleared + committed) so a retry no-ops");
        stored.Status.Should().Be(InterviewStatus.Scheduled,
            "the reminder job must not alter the interview lifecycle — only the reminder marker");
    }

    // ── Offer expiry reminder ───────────────────────────────────────────────

    [Fact]
    [Trait("TC", "TC-REC-007-05")]
    public async Task OfferExpiryReminderJob_ReExecution_SendsExactlyOneReminder_ISSUE116()
    {
        var offerId = SeedActiveOfferWithPendingReminder();
        var job = new OfferExpiryReminderJob(BuildScopeFactory());

        await job.RunAsync(_tenantId, offerId);
        await job.RunAsync(_tenantId, offerId);

        await _notifications.Received(1).NotifyOfferAsync(
            "offer-expiry-reminder",
            offerId,
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("TC", "TC-REC-007-05")]
    public async Task OfferExpiryReminderJob_ClaimsOnlyTheReminderMarker_ISSUE116()
    {
        var offerId = SeedActiveOfferWithPendingReminder();
        var job = new OfferExpiryReminderJob(BuildScopeFactory());

        await job.RunAsync(_tenantId, offerId);

        using var db = TestDbContextFactory.Create(_tenantId, _dbName);
        var stored = db.Offers.Single(o => o.Id == offerId);
        stored.ExpiryReminderJobId.Should().BeNull("the dispatched reminder must be claimed");
        stored.Status.Should().Be(OfferStatus.Sent,
            "the REMINDER job must not expire the offer — that is OfferExpiryJob's job");
        stored.ReminderJobId.Should().Be("expiry-job-1",
            "clearing the expiry-REMINDER marker must not clobber the auto-EXPIRE job id");
    }

    // ── Scaffolding ─────────────────────────────────────────────────────────

    // A real scope factory: each RunAsync opens its own scope and resolves a fresh AppDbContext over the
    // shared named InMemory store, so the second run re-reads COMMITTED state (not a warm change tracker).
    private IServiceScopeFactory BuildScopeFactory()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(_tenantId);
        tenantContext.IsResolved.Returns(true);
        tenantContext.IsSystemContext.Returns(false);

        var services = new ServiceCollection();
        services.AddSingleton(tenantContext);
        services.AddScoped(sp => TestDbContextFactory.Create(
            sp.GetRequiredService<ITenantContext>(), _dbName));
        // RLS increment 2c: the jobs wrap their body in ITenantJobRunner — register the real runner + an
        // empty IConfiguration (Rls:Enabled defaults false → runs the work directly, no tx/GUC).
        services.AddScoped<ITenantJobRunner, TenantJobRunner>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(_notifications);

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private Guid SeedScheduledInterviewWithPendingReminder()
    {
        var interviewId = Guid.NewGuid();
        using var db = TestDbContextFactory.Create(_tenantId, _dbName);

        db.Interviews.Add(new Interview
        {
            Id = interviewId,
            TenantId = _tenantId,
            ApplicantId = Guid.NewGuid(),
            VacancyId = Guid.NewGuid(),
            RoundNumber = 1,
            InterviewType = InterviewType.Video,
            ScheduledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            StartTime = new TimeOnly(10, 0),
            DurationMinutes = 60,
            VideoLink = "https://meet.example.test/abc",
            Status = InterviewStatus.Scheduled,
            // The marker InterviewService.ScheduleAsync persists alongside the row — a PENDING reminder.
            ReminderJobId = "reminder-job-1",
        });
        db.InterviewInterviewers.Add(new InterviewInterviewer
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            InterviewId = interviewId,
            EmployeeId = Guid.NewGuid(),
        });
        db.SaveChanges();

        return interviewId;
    }

    private Guid SeedActiveOfferWithPendingReminder()
    {
        var offerId = Guid.NewGuid();
        using var db = TestDbContextFactory.Create(_tenantId, _dbName);

        db.Offers.Add(new Offer
        {
            Id = offerId,
            TenantId = _tenantId,
            ApplicantId = Guid.NewGuid(),
            VacancyId = Guid.NewGuid(),
            OfferReferenceNumber = "OFF-0001",
            Status = OfferStatus.Sent, // IsActive
            OfferedPosition = "Engineer",
            SalaryAmount = 100_000m,
            Currency = "USD",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            PdfStorageKey = "offers/OFF-0001.pdf",
            SentAt = DateTime.UtcNow,
            // Two INDEPENDENT job ids (see Offer.ExpiryReminderJobId docs): the auto-expire job and the
            // expiry-reminder job. Only the latter is this job's marker.
            ReminderJobId = "expiry-job-1",
            ExpiryReminderJobId = "expiry-reminder-job-1",
        });
        db.SaveChanges();

        return offerId;
    }
}
