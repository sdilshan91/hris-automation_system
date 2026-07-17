using HRM.Application.Common.Interfaces;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Stale-goal nudge sweep (US-PRF-009 AC-5/FR-6/BR-4/NFR-5). Invoked once per active tenant by the daily Hangfire
/// recurring job (which sets the tenant context first). For the CURRENT tenant it reads the configurable stale
/// interval (<c>Tenant.StaleGoalNudgeDays</c>, default 14; 0 DISABLES the sweep, BR-4), then over the tenant's
/// active goals (Submitted/Acknowledged in an Active cycle) flags any goal whose latest update is older than the
/// interval — or that has no update yet, measured from goal-setting close — and nudges the employee via the
/// log-only performance notification seam (real-time/SignalR deferred, US-NTF-001). The goal's "Needs Attention"
/// state is derived LIVE from this same staleness rule on the read side (no extra column). All queries ride the EF
/// global query filter, so the sweep never crosses tenants (NFR-2). Mirrors the <c>PipReminderService</c> shape.
/// </summary>
public sealed class StaleGoalNudgeService : IStaleGoalNudgeService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IPerformanceNotificationService _notifications;
    private readonly ILogger<StaleGoalNudgeService> _logger;

    public StaleGoalNudgeService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IPerformanceNotificationService notifications,
        ILogger<StaleGoalNudgeService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<int> RunSweepAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return 0;

        // BR-4: 0 disables nudge notifications for the tenant.
        var nudgeDays = await _dbContext.Tenants.AsNoTracking().IgnoreQueryFilters()
            .Where(t => t.Id == _tenantContext.TenantId)
            .Select(t => (int?)t.StaleGoalNudgeDays)
            .FirstOrDefaultAsync(cancellationToken) ?? 14;
        if (nudgeDays <= 0)
            return 0;

        // Active goals in Active cycles only (tracking happens after goal-setting closes).
        var activeCycleIds = await _dbContext.AppraisalCycles.AsNoTracking()
            .Where(c => c.Status == AppraisalCycleStatus.Active)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        if (activeCycleIds.Count == 0)
            return 0;

        var goals = await _dbContext.Goals.AsNoTracking()
            .Where(g => activeCycleIds.Contains(g.CycleId)
                && (g.Status == GoalStatus.Submitted || g.Status == GoalStatus.Acknowledged))
            .ToListAsync(cancellationToken);
        if (goals.Count == 0)
            return 0;

        var goalIds = goals.Select(g => g.Id).ToList();

        // Latest-update timestamp per goal (a single grouped query).
        var latestByGoal = (await _dbContext.GoalProgressUpdates.AsNoTracking()
            .Where(u => goalIds.Contains(u.GoalId))
            .GroupBy(u => u.GoalId)
            .Select(grp => new { GoalId = grp.Key, Latest = grp.Max(u => u.CreatedAtUtc) })
            .ToListAsync(cancellationToken))
            .ToDictionary(x => x.GoalId, x => x.Latest);

        var cycleStarts = (await _dbContext.AppraisalCycles.AsNoTracking()
            .Where(c => activeCycleIds.Contains(c.Id))
            .Select(c => new { c.Id, c.StartDate, c.GoalSettingEnd })
            .ToListAsync(cancellationToken))
            .ToDictionary(c => c.Id, c => c.GoalSettingEnd == default ? c.StartDate : c.GoalSettingEnd);

        var dispatched = 0;
        var nudgedGoalIds = new List<Guid>();
        foreach (var goal in goals)
        {
            // ISSUE-142 (TC-009-04): idempotency — a goal already nudged on the current UTC date is skipped, so
            // a same-day re-run or Hangfire retry never double-notifies.
            if (goal.LastStaleNudgeAtUtc?.Date == nowUtc.Date)
                continue;

            DateTime? lastUpdated = latestByGoal.TryGetValue(goal.Id, out var ts) ? ts : null;
            cycleStarts.TryGetValue(goal.CycleId, out var since);

            if (!GoalProgressCalculator.IsStale(lastUpdated, since, nudgeDays, nowUtc))
                continue;

            var staleSinceDays = (int)(nowUtc - (lastUpdated ?? since)).TotalDays;

            // AC-5: nudge the employee. The "Needs Attention" manager flag is derived live on the read side.
            await _notifications.NotifyGoalProgressAsync(
                "goal-stale-nudge", goal.Id, goal.EmployeeId, goal.EmployeeId,
                $"{goal.Title}|{staleSinceDays}", cancellationToken);
            nudgedGoalIds.Add(goal.Id);
            dispatched++;
        }

        // ISSUE-142: stamp the nudge date on the goals just nudged so a same-day re-run skips them. A tracked
        // update + SaveChanges (works on both the InMemory test provider and Postgres; ExecuteUpdate does not).
        if (nudgedGoalIds.Count > 0)
        {
            var toMark = await _dbContext.Goals
                .Where(g => nudgedGoalIds.Contains(g.Id))
                .ToListAsync(cancellationToken);
            foreach (var g in toMark)
                g.LastStaleNudgeAtUtc = nowUtc;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Stale-goal nudge sweep. Dispatched={Count}, NudgeDays={Days}, TenantId={TenantId}",
            dispatched, nudgeDays, _tenantContext.TenantId);
        return dispatched;
    }
}
