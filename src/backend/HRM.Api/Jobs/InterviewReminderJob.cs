using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire job that fires ~24h (configurable) before an interview to remind all participants
/// (US-REC-005 FR-4/AC-2/NFR-3/NFR-4). It is TENANT-AWARE: the tenant id is passed in the job args
/// (NFR-4), and the job restores the tenant context for its scope so the EF global query filters apply
/// (mirrors <c>AutoClockOutJob.ProcessTenantAsync</c>). Idempotent (NFR-4): it no-ops if the interview is
/// missing, cancelled, or no longer Scheduled — and, since ISSUE-116, also if its <c>ReminderJobId</c> marker
/// is already null, which is what makes a Hangfire RETRY a no-op rather than a second reminder. Otherwise it
/// clears the marker and dispatches the reminder via <c>IRecruitmentNotificationService</c> — which in
/// production is <c>RealRecruitmentNotificationService</c> (REAL email + in-app), NOT a log-only seam.
///
/// <para><b>BUG-530 — retry on a failed dispatch.</b> The seam never throws, so before it returned a
/// <c>Result</c> this job could not tell a delivered reminder from a wholly failed one and every failure was
/// lost in silence. It now THROWS on a failed <c>Result</c>, which fails the Hangfire run and hands the retry
/// to Hangfire's existing automatic-retry machinery. Re-running is safe because the job is idempotent.</para>
///
/// <para><b>The limit, stated honestly:</b> this covers transient dispatch failures — the dominant real cause —
/// and nothing more. It is NOT at-least-once delivery: a process death between a successful dispatch and the
/// enclosing work is not recovered, because nothing durably records that a send is owed. A transactional
/// outbox would be at-least-once and was deliberately not built.</para>
/// </summary>
public sealed class InterviewReminderJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public InterviewReminderJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <param name="tenantId">The tenant the interview belongs to (NFR-4 — restores tenant context).</param>
    /// <param name="interviewId">The interview to remind participants about.</param>
    public async Task RunAsync(Guid tenantId, Guid interviewId)
    {
        using var scope = _scopeFactory.CreateScope();

        var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<IRecruitmentNotificationService>();

        // RLS increment 2c: run the tenant body via the shared runner so it sets the tenant context (and, gated
        // on Rls:Enabled, the app.current_tenant GUC) — this interview-by-id job stays inside the RLS backstop.
        await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}", async _ =>
        {
        // ISSUE-116 (NFR-4): TRACKED read (no AsNoTracking) — the reminder marker is cleared and committed
        // below, and that write is what makes a Hangfire retry a no-op instead of a second reminder.
        var interview = await dbContext.Interviews
            .Include(i => i.Interviewers)
            .FirstOrDefaultAsync(i => i.Id == interviewId);

        // Idempotent / defensive (NFR-4): only remind for a still-scheduled interview that still has a
        // PENDING reminder marker. ReminderJobId is set on schedule and swapped on reschedule, and is
        // cleared here as the reminder is dispatched — so a null marker means "already reminded" (or never
        // scheduled at all), and a Hangfire retry of this job no-ops instead of double-sending.
        if (interview is null || interview.Status != InterviewStatus.Scheduled || interview.ReminderJobId is null)
        {
            Log.Information(
                "InterviewReminderJob: skipping interview {InterviewId} for tenant {TenantId} (missing, not scheduled, or reminder already sent)",
                interviewId, tenantId);
            return;
        }

        // ISSUE-116: CLAIM the reminder BEFORE dispatching it, mirroring OfferExpiryJob/OfferExpiryReminderJob
        // (which commit their state change before notifying).
        //
        // Why not clear AFTER dispatch: the job cannot observe whether dispatch succeeded.
        // ISSUE-571: the marker is cleared only AFTER a confirmed-successful dispatch, further down.
        // The earlier version cleared it here, before dispatching, because the seam swallowed every failure
        // and returned an identical completed Task either way — "clear only on success" was genuinely not
        // expressible. BUG-530 removed that constraint by making the seam return a Result, so the premise
        // that forced clear-first no longer holds.

        var applicantEmail = await dbContext.Applicants
            .AsNoTracking()
            .Where(a => a.Id == interview.ApplicantId)
            .Select(a => a.Email)
            .FirstOrDefaultAsync() ?? string.Empty;

        var interviewerEmployeeIds = interview.Interviewers.Select(ii => ii.EmployeeId).ToList();

        var dispatch = await notifications.NotifyInterviewReminderAsync(
            interview.Id, interview.ApplicantId, interview.VacancyId, applicantEmail, interviewerEmployeeIds);

        // BUG-530: fail the run so Hangfire retries the dispatch. Throwing is the ONLY way to signal failure to
        // Hangfire — a job that returns normally is recorded as Succeeded, which is exactly how a lost candidate
        // reminder used to be reported as a delivered one.
        if (dispatch.IsFailure)
        {
            Log.Warning(
                "InterviewReminderJob: reminder dispatch FAILED for interview {InterviewId} (tenant {TenantId}); " +
                "failing the run so Hangfire retries. Error={Error}",
                interview.Id, tenantId, dispatch.Error);

            throw new InvalidOperationException(
                $"Interview reminder dispatch failed for interview {interview.Id} (tenant {tenantId}): {dispatch.Error}");
        }

        // ISSUE-571: clear the marker ONLY now that the dispatch is confirmed delivered. A failed dispatch
        // leaves it intact, so the Hangfire retry passes the guard above and genuinely re-sends — which is
        // what BUG-530's throw exists to trigger and could not do while the marker was cleared first.
        // Residual window, stated plainly: a process death between this dispatch and this SaveChanges leaves
        // the marker set, so the retry re-sends and the candidate gets ONE duplicate. That is a far narrower
        // and less harmful failure than the previous behaviour, where any dispatch failure lost the reminder
        // permanently and silently. Only an outbox removes it entirely (BUG-530).
        interview.ReminderJobId = null;
        await dbContext.SaveChangesAsync();

        Log.Information(
            "InterviewReminderJob: sent reminder for interview {InterviewId} (tenant {TenantId}) to applicant + {Count} interviewer(s)",
            interview.Id, tenantId, interviewerEmployeeIds.Count);
        });
    }
}
