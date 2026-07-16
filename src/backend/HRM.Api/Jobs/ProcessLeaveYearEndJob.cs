using HRM.Application.Common.Interfaces;
using HRM.Domain.Leave;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Retry;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire recurring job that processes year-end leave carry-forward and forfeiture for all
/// tenants (US-LV-008 FR-2, AC-1/AC-2/AC-4, BR-1/BR-2/BR-6).
///
/// Runs DAILY and processes each tenant on the first days of THEIR OWN leave year — which is not
/// 1 January for a fiscal tenant (ISSUE-305: an Apr-Mar tenant's year ends 31 March). The old
/// "0 2 1 1 *" cron could never have run their year-end at all, whatever the job computed
/// internally. <see cref="ClosingLeaveYearIfDue"/> is the per-tenant boundary check; on the ~358
/// days no tenant is due the job selects nothing and exits.
///
/// For each due tenant it sets the tenant context and processes the just-closed year: for each
/// employee × applicable leave type it computes the unused balance, carries forward
/// MIN(unused, limit), forfeits (expires or encashes) the remainder, and writes a carry-forward
/// tracking row.
///
/// Idempotent (NFR-3): a re-run skips any pair already tracked for the closing year — which is what
/// makes both the daily cadence and the multi-day grace window safe. Per-tenant
/// work is wrapped in a Polly retry policy (NFR-4: max 3 retries, exponential backoff) so a
/// transient failure for one tenant is retried before the loop logs it and moves on — one tenant's
/// permanent failure never aborts the batch (NFR-2). Mirrors LeaveAccrualJob / HolidayRecurrenceJob.
/// </summary>
public sealed class ProcessLeaveYearEndJob
{
    /// <summary>
    /// How many days after a tenant's leave year opens the year-end sweep still runs for them. Wide enough
    /// to survive a few consecutive failed/missed daily runs; the work is idempotent so repeats are no-ops.
    /// </summary>
    private const int GraceDays = 7;

    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// ISSUE-305: the job's behaviour is now DATE-DEPENDENT — it only works for tenants inside their own
    /// year-end window — so "today" must be injectable or the job is untestable on 358 days of the year.
    /// Trailing-optional (mirrors <c>NotificationPreferenceService</c>) so production and every existing
    /// fixture keep the real clock without touching a constructor call.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    // NFR-4: retry transient per-tenant failures with exponential backoff (2^n s), max 3 attempts.
    // Mirrors the canonical Polly idiom in PayslipDistributionRunner / Program.cs ResilientClient.
    private static readonly AsyncRetryPolicy RetryPolicy = Policy
        .Handle<Exception>()
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
            onRetry: (ex, delay, attempt, _) => Log.Warning(ex,
                "ProcessLeaveYearEndJob: transient failure, retry {Attempt}/3 in {DelaySeconds}s",
                attempt, delay.TotalSeconds));

    public ProcessLeaveYearEndJob(IServiceScopeFactory scopeFactory, TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }


    /// <summary>
    /// ISSUE-305: the leave year this tenant is currently CLOSING, or null when today is not in their year-end
    /// window. A tenant's new leave year opens on the 1st of their <c>FiscalYearStartMonth</c>; the year that
    /// just closed is the one before it.
    ///
    /// <para><see cref="GraceDays"/> rather than "exactly day 1": a run that fails or is missed on the opening
    /// day would otherwise skip that tenant's year-end for a WHOLE year. Repeats inside the window are
    /// harmless — the per-pair work is idempotent.</para>
    /// </summary>
    internal static int? ClosingLeaveYearIfDue(DateOnly today, int fiscalYearStartMonth)
    {
        var currentLeaveYear = LeaveYear.LabelFor(today, fiscalYearStartMonth);
        var start = LeaveYear.StartOf(currentLeaveYear, fiscalYearStartMonth);

        // No lower bound: LabelFor returns the year that CONTAINS today, so its start is always on-or-before
        // today and this is never negative (LeaveYearTests.LabelFor_AndBoundsFor_Agree pins that contract).
        var daysSinceOpen = today.DayNumber - start.DayNumber;
        return daysSinceOpen < GraceDays ? currentLeaveYear - 1 : null;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting ProcessLeaveYearEndJob");

        // ISSUE-305: this job used to fire ONLY on 1 January and assume `UtcNow.Year - 1` was the closing
        // leave year. Both are wrong for a fiscal tenant: an Apr-Mar tenant's year ends 31 March, so a 1-Jan
        // cron would have processed their year-end on the wrong date AND for the wrong year — and no constant
        // swap could fix the schedule. It now runs DAILY and each tenant is processed only when TODAY falls in
        // the opening window of THEIR new leave year.
        //
        // Safe to run daily because the per-pair work is already idempotent (NFR-3: a re-run skips any pair
        // already tracked for the closing year) — the same property that made the old comment say "a
        // daily-or-later cadence in early January is safe".
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        List<(Guid Id, int FiscalYearStartMonth)> tenants;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenants = (await dbContext.Tenants
                .Where(t => !t.IsDeleted && (t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial))
                .Select(t => new { t.Id, t.FiscalYearStartMonth })
                .ToListAsync())
                .Select(t => (t.Id, t.FiscalYearStartMonth))
                .ToList();
        }

        // Only the tenants whose new leave year opened within the grace window. The window (rather than "is
        // today exactly day 1") means a missed or failed run still catches up on a later day instead of
        // skipping that tenant's year-end for a whole year.
        var due = tenants
            .Select(t => (t.Id, ClosingYear: ClosingLeaveYearIfDue(today, t.FiscalYearStartMonth)))
            .Where(t => t.ClosingYear is not null)
            .Select(t => (t.Id, ClosingYear: t.ClosingYear!.Value))
            .ToList();

        Log.Information(
            "ProcessLeaveYearEndJob: {DueCount} of {TenantCount} tenants are in their year-end window today",
            due.Count, tenants.Count);

        foreach (var (tenantId, fromYear) in due)
        {
            try
            {
                // Retry the per-tenant unit of work (fresh scope each attempt) on transient failure.
                await RetryPolicy.ExecuteAsync(async () =>
                {
                    using var scope = _scopeFactory.CreateScope();

                    var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
                    var service = scope.ServiceProvider.GetRequiredService<ILeaveCarryForwardService>();

                    // RLS increment 2c: run the per-tenant body via the shared runner so it sets the tenant
                    // context (and, gated on Rls:Enabled, the app.current_tenant GUC). The outer Polly retry
                    // re-runs the whole unit (fresh scope + runner) on a transient failure.
                    await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}", async _ =>
                    {
                        var result = await service.ProcessYearEndAsync(fromYear);
                        Log.Information(
                            "ProcessLeaveYearEndJob: Tenant {TenantId} carried forward {Count} pairs for year {Year}",
                            tenantId, result.IsSuccess ? result.Value : 0, fromYear);
                    });
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    "ProcessLeaveYearEndJob: Failed to process tenant {TenantId} after retries", tenantId);
                // Continue with other tenants; don't fail the whole job.
            }
        }

        Log.Information("Completed ProcessLeaveYearEndJob");
    }
}
