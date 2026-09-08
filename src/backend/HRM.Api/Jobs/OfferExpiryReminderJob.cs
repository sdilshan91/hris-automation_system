using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire job that fires N days BEFORE an offer's expiry to nudge the candidate (+ recruiter pool) that the
/// offer is about to lapse (US-REC-007 FR-7/AC-4). Sibling of <see cref="OfferExpiryJob"/> (which auto-expires
/// AT the boundary). TENANT-AWARE: the tenant id is passed in the job args and the job restores the tenant
/// context for its scope so the EF global query filters apply. Idempotent: it no-ops if the offer is missing
/// or no longer Draft/Sent (already responded/withdrawn/expired) — this is what prevents a reminder firing
/// after the candidate already accepted/withdrew — and, since ISSUE-116, also if its
/// <c>ExpiryReminderJobId</c> marker is already null, which makes a Hangfire RETRY a no-op rather than a
/// second reminder. Otherwise it emits the reminder notification
/// (candidate + recruiter pool) via <c>NotifyOfferAsync("offer-expiry-reminder", …)</c> and clears the
/// reminder job id. Unlike the expiry job it does NOT change the offer status.
///
/// <para><b>BUG-530 — retry on a failed dispatch.</b> The seam never throws, so before it returned a
/// <c>Result</c> this job could not tell a delivered expiry warning from a wholly failed one. It now THROWS on
/// a failed <c>Result</c>, failing the Hangfire run so Hangfire's existing automatic retries re-run the
/// dispatch. Re-running is safe: the <c>IsActive</c> guard above already makes the job idempotent, and the
/// marker clear is a no-op on the second pass.</para>
///
/// <para><b>The limit, stated honestly:</b> this is retry-on-detected-failure, NOT at-least-once delivery. A
/// process death between a successful dispatch and the surrounding work is not recovered, because nothing
/// durably records that a send is owed. A transactional outbox would be; it was deliberately not built.</para>
/// </summary>
public sealed class OfferExpiryReminderJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public OfferExpiryReminderJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <param name="tenantId">The tenant the offer belongs to (restores tenant context).</param>
    /// <param name="offerId">The offer to remind about if still active.</param>
    public async Task RunAsync(Guid tenantId, Guid offerId)
    {
        using var scope = _scopeFactory.CreateScope();

        var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<IRecruitmentNotificationService>();

        // RLS increment 2c: run the tenant body via the shared runner so it sets the tenant context (and, gated
        // on Rls:Enabled, the app.current_tenant GUC) — this offer-by-id job stays inside the RLS backstop.
        await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}", async _ =>
        {
        var offer = await dbContext.Offers.FirstOrDefaultAsync(o => o.Id == offerId);

        // Idempotent / defensive: only remind about a still-active (Draft/Sent) offer. If it has already been
        // accepted/declined/withdrawn/expired, do NOT send a "your offer is expiring soon" nudge.
        // ISSUE-116 (NFR-4): the ExpiryReminderJobId check is the RETRY guard. This job already cleared the
        // marker below but never READ it, so a Hangfire retry of a still-active offer sent a SECOND reminder.
        // The marker is set on send and cleared on respond/withdraw/supersede, so null means "already
        // reminded" (or no reminder pending) and the retry now no-ops.
        if (offer is null || !offer.IsActive || offer.ExpiryReminderJobId is null)
        {
            Log.Information(
                "OfferExpiryReminderJob: skipping offer {OfferId} for tenant {TenantId} (missing, no longer active, or reminder already sent)",
                offerId, tenantId);
            return;
        }

        // ISSUE-571: the marker is cleared only AFTER a confirmed-successful dispatch, further down — see the
        // matching note in InterviewReminderJob. Clearing it here would make BUG-530's retry inert.
        var applicantEmail = await dbContext.Applicants
            .AsNoTracking()
            .Where(a => a.Id == offer.ApplicantId)
            .Select(a => a.Email)
            .FirstOrDefaultAsync() ?? string.Empty;

        var dispatch = await notifications.NotifyOfferAsync(
            "offer-expiry-reminder", offer.Id, offer.ApplicantId, offer.VacancyId, applicantEmail);

        // BUG-530: fail the run so Hangfire retries. A job that returns normally is recorded as Succeeded, which is
        // how a candidate's lost "your offer expires soon" email used to be indistinguishable from a delivered one.
        if (dispatch.IsFailure)
        {
            Log.Warning(
                "OfferExpiryReminderJob: reminder dispatch FAILED for offer {OfferId} (tenant {TenantId}); " +
                "failing the run so Hangfire retries. Error={Error}",
                offer.Id, tenantId, dispatch.Error);

            throw new InvalidOperationException(
                $"Offer expiry-reminder dispatch failed for offer {offer.Id} (tenant {tenantId}): {dispatch.Error}");
        }

        // ISSUE-571: clear ONLY now that the dispatch is confirmed delivered, so a failed dispatch leaves the
        // marker intact and the Hangfire retry above genuinely re-sends. Residual window: a process death
        // between dispatch and this save yields ONE duplicate — far narrower than losing the reminder silently.
        offer.ExpiryReminderJobId = null;
        await dbContext.SaveChangesAsync();

        Log.Information(
            "OfferExpiryReminderJob: reminder sent for offer {OfferId} (tenant {TenantId})", offer.Id, tenantId);
        });
    }
}
