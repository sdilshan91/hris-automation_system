// ============================================================================
// BUG-530 (US-REC-005 NFR-4, US-REC-007 FR-7/AC-4): the recruitment notification seam used to swallow every
// delivery failure, returning an identical completed Task whether every email was delivered or every one failed.
// No caller — job or service — could detect, let alone retry, a lost candidate email.
//
// The seam now returns a Result. These tests prove the half that actually buys something: the two Hangfire
// reminder jobs FAIL THEIR RUN on a failed dispatch, which is the only signal Hangfire accepts (a job that
// returns normally is recorded as Succeeded), so its existing automatic-retry machinery re-runs the dispatch.
//
// Each defect arm is paired with its success arm, so the tests fail if the job throws unconditionally as well
// as if it never throws — a one-armed test here would pass against a job that always threw.
//
// LIMIT, deliberately not overclaimed: this is retry-on-DETECTED-failure, not at-least-once delivery. Nothing
// durably records that a send is owed, so process death between a successful dispatch and the surrounding work
// is not recovered. An outbox would be at-least-once; it was consciously not built.
// ============================================================================

using FluentAssertions;
using HRM.Api.Jobs;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class RecruitmentNotificationRetryTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _vacancyId = Guid.NewGuid();
    private readonly Guid _applicantId = Guid.NewGuid();
    private readonly Guid _interviewId = Guid.NewGuid();
    private readonly Guid _offerId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    // ── The offer expiry-reminder job ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("TC", "TC-REC-007-21")]
    public async Task OfferExpiryReminderJob_DispatchFails_FailsTheRunSoHangfireRetries_BUG530()
    {
        SeedOffer(OfferStatus.Sent);
        var notifications = RecruitmentNotifications.Failing("smtp unavailable");
        var provider = BuildProvider(notifications);
        var job = new OfferExpiryReminderJob(provider.GetRequiredService<IServiceScopeFactory>());

        var act = () => job.RunAsync(_tenantId, _offerId);

        // Hangfire only retries a job that FAILS. Before BUG-530 the seam reported nothing, this returned
        // normally, Hangfire recorded Succeeded, and the candidate's expiry warning was lost in silence.
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*expiry-reminder dispatch failed*")
            .WithMessage("*smtp unavailable*");
    }

    [Fact]
    [Trait("TC", "TC-REC-007-21")]
    public async Task OfferExpiryReminderJob_DispatchSucceeds_CompletesTheRun_BUG530()
    {
        SeedOffer(OfferStatus.Sent);
        var notifications = RecruitmentNotifications.Succeeding();
        var provider = BuildProvider(notifications);
        var job = new OfferExpiryReminderJob(provider.GetRequiredService<IServiceScopeFactory>());

        var act = () => job.RunAsync(_tenantId, _offerId);

        await act.Should().NotThrowAsync("a delivered reminder must NOT be re-queued by Hangfire");
        await notifications.Received(1).NotifyOfferAsync(
            "offer-expiry-reminder", _offerId, _applicantId, _vacancyId, Arg.Any<string>());
    }

    [Fact]
    [Trait("TC", "TC-REC-007-21")]
    public async Task OfferExpiryReminderJob_InactiveOffer_DoesNotFailTheRun_BUG530()
    {
        // The candidate already accepted, so the job's idempotency guard no-ops BEFORE any dispatch. Nothing was
        // owed, so nothing failed — retrying this forever would be pure noise in Hangfire's failed-job list.
        SeedOffer(OfferStatus.Accepted);
        var notifications = RecruitmentNotifications.Failing();
        var provider = BuildProvider(notifications);
        var job = new OfferExpiryReminderJob(provider.GetRequiredService<IServiceScopeFactory>());

        var act = () => job.RunAsync(_tenantId, _offerId);

        await act.Should().NotThrowAsync();
        await notifications.DidNotReceive().NotifyOfferAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    // ── The interview reminder job ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("TC", "TC-REC-005-20")]
    public async Task InterviewReminderJob_DispatchFails_FailsTheRunSoHangfireRetries_BUG530()
    {
        SeedInterview(InterviewStatus.Scheduled);
        var notifications = RecruitmentNotifications.Failing("dispatcher down");
        var provider = BuildProvider(notifications);
        var job = new InterviewReminderJob(provider.GetRequiredService<IServiceScopeFactory>());

        var act = () => job.RunAsync(_tenantId, _interviewId);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Interview reminder dispatch failed*")
            .WithMessage("*dispatcher down*");
    }

    [Fact]
    [Trait("TC", "TC-REC-005-20")]
    public async Task InterviewReminderJob_AfterAFailedDispatch_TheRetryActuallyReSends_ISSUE571()
    {
        // ISSUE-571: BUG-530's throw only buys something if the RETRY can re-send. While the marker was
        // cleared BEFORE dispatch, the retry hit the null-marker guard and returned silently — the throw
        // failed the run but recovered nothing. This asserts the recovery itself, not just the throw.
        SeedInterview(InterviewStatus.Scheduled);
        var failing = RecruitmentNotifications.Failing("dispatcher down");
        var job1 = new InterviewReminderJob(BuildProvider(failing).GetRequiredService<IServiceScopeFactory>());

        await ((Func<Task>)(() => job1.RunAsync(_tenantId, _interviewId)))
            .Should().ThrowAsync<InvalidOperationException>();

        // The marker MUST survive a failed dispatch, or the retry below cannot pass the guard.
        using (var db = RawDb())
        {
            db.Interviews.Single(i => i.Id == _interviewId).ReminderJobId
                .Should().NotBeNull("a failed dispatch must leave the reminder still owed");
        }

        // Hangfire's retry: a second run against a healthy seam must genuinely dispatch.
        var healthy = RecruitmentNotifications.Succeeding();
        var job2 = new InterviewReminderJob(BuildProvider(healthy).GetRequiredService<IServiceScopeFactory>());
        await job2.RunAsync(_tenantId, _interviewId);

        await healthy.Received(1).NotifyInterviewReminderAsync(
            _interviewId, _applicantId, _vacancyId, Arg.Any<string>(),
            Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());

        // And only after that success is the reminder marked done, so a THIRD run no-ops.
        using (var db = RawDb())
        {
            db.Interviews.Single(i => i.Id == _interviewId).ReminderJobId
                .Should().BeNull("a delivered reminder must not be sent again");
        }
    }

    [Fact]
    [Trait("TC", "TC-REC-005-20")]
    public async Task InterviewReminderJob_DispatchSucceeds_CompletesTheRun_BUG530()
    {
        SeedInterview(InterviewStatus.Scheduled);
        var notifications = RecruitmentNotifications.Succeeding();
        var provider = BuildProvider(notifications);
        var job = new InterviewReminderJob(provider.GetRequiredService<IServiceScopeFactory>());

        var act = () => job.RunAsync(_tenantId, _interviewId);

        await act.Should().NotThrowAsync("a delivered reminder must NOT be re-queued by Hangfire");
        await notifications.Received(1).NotifyInterviewReminderAsync(
            _interviewId, _applicantId, _vacancyId, Arg.Any<string>(), Arg.Any<IReadOnlyList<Guid>>());
    }

    [Fact]
    [Trait("TC", "TC-REC-005-20")]
    public async Task InterviewReminderJob_CancelledInterview_DoesNotFailTheRun_BUG530()
    {
        SeedInterview(InterviewStatus.Cancelled);
        var notifications = RecruitmentNotifications.Failing();
        var provider = BuildProvider(notifications);
        var job = new InterviewReminderJob(provider.GetRequiredService<IServiceScopeFactory>());

        var act = () => job.RunAsync(_tenantId, _interviewId);

        await act.Should().NotThrowAsync();
        await notifications.DidNotReceive().NotifyInterviewReminderAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    // ── The seam's own contract: never throws, but no longer lies ───────────────────────────────

    [Fact]
    [Trait("TC", "TC-REC-007-21")]
    public async Task LogOnlySeam_HonoursTheContract_ReportsSuccess_BUG530()
    {
        // The log-only seam is the DI default outside the notification-enabled configuration. If it returned a
        // failure (or a null Result), every reminder job would fail and retry forever on a healthy system.
        var seam = new LogOnlyRecruitmentNotificationService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LogOnlyRecruitmentNotificationService>.Instance);

        var offerResult = await seam.NotifyOfferAsync(
            "offer-expiry-reminder", _offerId, _applicantId, _vacancyId, "candidate@example.test");
        var interviewResult = await seam.NotifyInterviewReminderAsync(
            _interviewId, _applicantId, _vacancyId, "candidate@example.test", []);

        offerResult.IsSuccess.Should().BeTrue();
        interviewResult.IsSuccess.Should().BeTrue();
    }

    // ── Scaffolding ─────────────────────────────────────────────────────────────────────────────

    private ServiceProvider BuildProvider(IRecruitmentNotificationService notifications)
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        // RLS increment 2c: both jobs wrap their body in ITenantJobRunner — register the real runner + an empty
        // IConfiguration (Rls:Enabled defaults false → runs the work directly, no tx/GUC).
        services.AddScoped<ITenantJobRunner, TenantJobRunner>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(notifications);
        return services.BuildServiceProvider();
    }

    private void SeedApplicant(AppDbContext db) => db.Applicants.Add(new Applicant
    {
        Id = _applicantId,
        TenantId = _tenantId,
        VacancyId = _vacancyId,
        ApplicationReferenceNumber = "APP-2026-0001",
        FirstName = "Ada",
        LastName = "Lovelace",
        Email = "ada@example.test",
        Stage = ApplicantStage.Offer,
        Source = ApplicationSource.Public,
        AppliedAt = DateTime.UtcNow,
    });

    private void SeedOffer(OfferStatus status)
    {
        using var db = RawDb();
        SeedApplicant(db);
        db.Offers.Add(new Offer
        {
            Id = _offerId,
            TenantId = _tenantId,
            VacancyId = _vacancyId,
            ApplicantId = _applicantId,
            OfferReferenceNumber = "OFR-2026-0001",
            OfferedPosition = "Backend Engineer",
            SalaryAmount = 120000m,
            Currency = "USD",
            SalaryFrequency = SalaryFrequency.Annual,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            Status = status,
            ExpiryReminderJobId = "rem-job-1",
        });
        db.SaveChanges();
    }

    private void SeedInterview(InterviewStatus status)
    {
        using var db = RawDb();
        SeedApplicant(db);
        db.Interviews.Add(new Interview
        {
            Id = _interviewId,
            TenantId = _tenantId,
            VacancyId = _vacancyId,
            ApplicantId = _applicantId,
            ScheduledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            StartTime = new TimeOnly(10, 0),
            InterviewType = InterviewType.Video,
            Status = status,
            // ISSUE-116 guards on a PENDING reminder marker, and InterviewService.cs:136 always sets one
            // when it schedules. Without it the job correctly no-ops and never reaches the dispatch these
            // BUG-530 tests are about — mirrors the offer seed's ExpiryReminderJobId above.
            ReminderJobId = "int-rem-job-1",
        });
        db.SaveChanges();
    }

    private AppDbContext RawDb()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(_tenantId);
        tenantContext.IsResolved.Returns(true);
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, tenantContext);
    }
}
