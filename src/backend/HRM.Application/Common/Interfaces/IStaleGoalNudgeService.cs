namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Stale-goal nudge sweep (US-PRF-009 AC-5/FR-6/BR-4/NFR-5). Invoked once per active tenant by the daily Hangfire
/// recurring job (which sets the tenant context first). For the CURRENT tenant it detects active goals whose latest
/// progress update is older than the tenant-configurable stale interval (<c>Tenant.StaleGoalNudgeDays</c>, default
/// 14; 0 DISABLES the sweep, BR-4) — counting goals with NO update yet from the cycle's goal-setting close — and:
///   - sends the employee a nudge notification ("You haven't updated progress on [Goal] in [X] days", AC-5), and
///   - flags the goal "Needs Attention" for the manager's dashboard (derived live from staleness, no extra column).
/// Every query rides the EF global query filter, so the sweep never crosses tenants (NFR-2). Mirrors the
/// <c>IPipReminderService</c> sweep shape.
/// </summary>
public interface IStaleGoalNudgeService
{
    /// <summary>
    /// Runs the stale-goal nudge sweep for the CURRENT tenant context. <paramref name="nowUtc"/> is injected for
    /// testability. Returns the number of nudge notifications dispatched. Returns 0 immediately when the tenant has
    /// the sweep disabled (StaleGoalNudgeDays == 0, BR-4) or the tenant context is not resolved.
    /// </summary>
    Task<int> RunSweepAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
