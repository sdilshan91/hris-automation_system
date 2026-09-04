using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Persistence;

/// <summary>
/// BUG-307 — the one place a tenant's effective plan limit is looked up.
/// </summary>
/// <remarks>
/// <para>
/// <b>The bug this exists to kill.</b> Ten call sites across ten files each hand-wrote:
/// </para>
/// <code>
/// var planValue = await db.SubscriptionPlans.Where(p =&gt; p.Code == tenant.PlanId)
///                         .Select(p =&gt; (long?)p.SomeLimit).FirstOrDefaultAsync(ct);
/// </code>
/// <para>
/// <c>FirstOrDefaultAsync</c> returns <c>null</c> in <b>two completely different situations</b>: the plan row
/// does not exist, and the plan row exists with a <c>NULL</c> limit. Both then flow into
/// <see cref="PlanLimitResolver"/> as "unlimited". They are not the same thing — the <c>enterprise</c> plan
/// genuinely ships <c>max_employees = NULL</c>, so NULL is a legitimate "unlimited", which is precisely why
/// no call site could tell a deliberate unlimited from a broken <c>plan_id</c>.
/// </para>
/// <para>
/// Measured, not theorised: 2 of 3 tenants carried <c>plan_id = 'default'</c>, matching no plan, with a NULL
/// snapshot as well — so every paid cap silently resolved to unlimited, with no error and no log. A
/// revenue-affecting rule that fails open, invisibly.
/// </para>
/// <para>
/// <b>Why centralised rather than fixed ten times.</b> This is the repo's systemic S-1 shape — many
/// hand-written copies of one rule with nothing checking they agree. These very paths already drifted once;
/// <c>BulkEmployeeImportService</c>'s own comment records it: <i>"three paths, three different answers about
/// one limit."</i> A tenth copy of the fix would have been the eleventh copy of the problem.
/// </para>
/// </remarks>
public static class PlanLimitLookup
{
    /// <summary>
    /// A resolved limit that still knows whether the tenant's plan actually exists.
    /// </summary>
    /// <param name="PlanExists">False when <c>tenant.PlanId</c> matches no <see cref="SubscriptionPlan"/>.</param>
    /// <param name="Value">The effective cap; <c>null</c> means unlimited (only meaningful when resolvable).</param>
    /// <param name="Source">Whether the value came from the plan or a per-tenant override.</param>
    public readonly record struct EffectivePlanLimit(
        bool PlanExists,
        long? Value,
        PlanLimitResolver.LimitSource Source)
    {
        /// <summary>
        /// Plans ARE configured for this deployment, but the tenant's <c>plan_id</c> matches none of them and
        /// no override supplies a value — a CONFIGURATION ERROR, not an unlimited allowance. Callers must
        /// refuse rather than permit: this is the fail-open that BUG-307 is about.
        ///
        /// <para>Deliberately NOT triggered when <c>subscription_plans</c> is empty: a deployment that
        /// configures no plans is not using plan-based limiting, so there is nothing to enforce.</para>
        /// </summary>
        public bool IsConfigurationError => !PlanExists && Source != PlanLimitResolver.LimitSource.Override;

        /// <summary>
        /// Genuinely unlimited — a resolvable plan (or an explicit override) that says "no cap".
        /// Distinct from <see cref="IsConfigurationError"/>, which merely looks the same in the old code.
        /// </summary>
        public bool IsUnlimited => !IsConfigurationError && Value is null;

        /// <summary>
        /// The cap the system ACTUALLY ENFORCES, given the tenant's denormalized snapshot column
        /// (<c>Tenant.MaxEmployees</c> / <c>MaxWorkflows</c> / <c>MaxCustomFields</c> / ...).
        /// Precedence: <b>override &gt; plan &gt; snapshot</b>. An override wins outright — a null override
        /// value is a deliberate "no cap", so it must NOT fall through to a stale snapshot. Otherwise the live
        /// plan value wins, and the snapshot is only a fallback for tenants provisioned before the plan
        /// defined that limit.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why this is a method and not six copies of one ternary.</b> It was five copies
        /// (<c>EmployeeService</c>, <c>BulkEmployeeImportService</c>, <c>UserManagementService</c>,
        /// <c>WorkflowService</c>, <c>CustomFieldService</c>) and <c>PlatformMonitoringService</c> hand-wrote a
        /// SIXTH with the precedence <b>INVERTED</b> — <c>snapshot ?? plan</c>. So the System Admin dashboard
        /// reported one cap while every enforcement gate applied another, and no staleness was required to
        /// make them diverge: a snapshot that merely DIFFERS from the plan was enough. A dashboard that
        /// contradicts the system's own enforcement is worse than no dashboard. There is now one answer to
        /// "what is this tenant's cap", and it lives here.
        /// </para>
        /// <para>
        /// Callers must still check <see cref="IsConfigurationError"/> FIRST: this method cannot distinguish
        /// "unlimited" from "unresolvable plan_id", which is precisely the collapse BUG-307 is about.
        /// </para>
        /// </remarks>
        /// <param name="snapshot">The tenant's denormalized limit column; null when never stamped.</param>
        public long? WithSnapshotFallback(long? snapshot)
            => Source == PlanLimitResolver.LimitSource.Override
                ? Value                 // override wins (null = a deliberate unlimited, never a snapshot fallback)
                : Value ?? snapshot;    // else the live plan value, else the snapshot
    }

    /// <summary>
    /// The in-memory half of <c>ResolveAsync</c>, for callers that have ALREADY batch-loaded the plan rows and
    /// the tenant's overrides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>PlatformMonitoringService</c> renders every tenant on one dashboard and pre-loads
    /// <c>subscription_plans</c> and <c>plan_limit_overrides</c> into dictionaries precisely so it does not
    /// issue three queries per tenant per limit key. Forcing it through the async overload would turn one
    /// dashboard read into an N&#215;4&#215;3 query storm — so it hand-wrote the resolution instead, and the
    /// hand-written copy is what discarded <see cref="EffectivePlanLimit.PlanExists"/> (BUG-473): an
    /// unresolvable <c>plan_id</c> rendered as "unlimited" on the very dashboard an operator would consult
    /// while the enforcement gates were returning 403 for that same tenant.
    /// </para>
    /// <para>
    /// This method exists so the "plans exist but this tenant points at none of them" rule has exactly ONE
    /// implementation regardless of how the rows were fetched; <see cref="ResolveCoreAsync"/> delegates to it
    /// rather than repeating it.
    /// </para>
    /// </remarks>
    /// <param name="anyPlansConfigured">
    /// Whether the deployment has ANY subscription plans at all. False ⇒ plan-based limiting is simply not in
    /// use, which is not a misconfiguration — see <see cref="EffectivePlanLimit.IsConfigurationError"/>.
    /// </param>
    /// <param name="planFound">Whether a plan row matched the tenant's <c>plan_id</c>.</param>
    /// <param name="planValue">That plan's value for this key; null = unlimited, or no plan row at all.</param>
    public static EffectivePlanLimit ResolveFromLoaded(
        string limitKey,
        bool anyPlansConfigured,
        bool planFound,
        long? planValue,
        IEnumerable<PlanLimitOverride>? overrides,
        DateTime nowUtc)
    {
        var resolved = PlanLimitResolver.Resolve(limitKey, planValue, overrides, nowUtc);

        return new EffectivePlanLimit(
            // "No plans configured anywhere" counts as resolvable: nothing to enforce, nothing broken.
            PlanExists: planFound || !anyPlansConfigured,
            Value: resolved.Value,
            Source: resolved.Source);
    }

    /// <summary>
    /// Resolves <paramref name="limitKey"/> for <paramref name="tenant"/>, preserving the distinction between
    /// "plan not found" and "plan found, no cap".
    /// </summary>
    /// <remarks>
    /// An applicable non-expired override WINS even when the plan is missing: an override is a deliberate,
    /// per-tenant decision, so it is a valid answer regardless of whether the plan row resolves.
    /// </remarks>
    /// <summary>
    /// ISSUE-388 — the strictest value any configured plan defines for a limit, or <see langword="null"/> if
    /// no plan defines one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the call sites whose method returns a bare <c>int</c>/<c>long</c>/<c>void</c>, "fail closed" cannot
    /// mean "return an error" — there is no channel to return one on. It has to mean <b>return the most
    /// restrictive defensible value</b>, which is what this supplies.
    /// </para>
    /// <para>
    /// <b>Why not zero.</b> Zero is the strictest reading, and it was rejected deliberately: for the email
    /// dispatcher it would silently stop ALL outbound mail, turning a mis-set <c>plan_id</c> into an incident
    /// of its own. Falling back to the tightest plan a deployment actually sells enforces a REAL cap instead
    /// of none, without bricking the feature.
    /// </para>
    /// <para>
    /// This is a backstop that should never fire: <c>DbInitializer.EnsureResolvablePlanIdAsync</c> repoints
    /// unresolvable plan ids at startup. It exists because "should never happen" is not a guarantee.
    /// </para>
    /// </remarks>
    public static async Task<long?> StrictestConfiguredAsync(
        AppDbContext db,
        Func<SubscriptionPlan, long?> planSelector,
        CancellationToken cancellationToken = default)
    {
        // subscription_plans is a tiny lookup table, so materialising it keeps the selector usable in memory
        // rather than forcing every caller to hand-write a translatable projection.
        var plans = await db.SubscriptionPlans.AsNoTracking().ToListAsync(cancellationToken);

        long? strictest = null;
        foreach (var plan in plans)
        {
            // A null limit means UNLIMITED for that plan, so it is the opposite of restrictive — skip it.
            if (planSelector(plan) is not { } value)
                continue;
            if (strictest is null || value < strictest)
                strictest = value;
        }

        return strictest;
    }

    /// <summary>
    /// Overload for callers that hold a tenant's id and plan code WITHOUT the full <see cref="Tenant"/>
    /// entity — several project only the columns they need (e.g. <c>new { t.PlanId, t.MaxWorkflows }</c>).
    /// Forcing them to materialise the whole entity just to reach this method would trade a real query cost
    /// for nothing; the lookup only ever needed these two values.
    /// </summary>
    public static Task<EffectivePlanLimit> ResolveAsync(
        AppDbContext db,
        Guid tenantId,
        string? planId,
        string limitKey,
        Func<SubscriptionPlan, long?> planSelector,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
        => ResolveCoreAsync(db, tenantId, planId, limitKey, planSelector, nowUtc, cancellationToken);

    public static Task<EffectivePlanLimit> ResolveAsync(
        AppDbContext db,
        Tenant tenant,
        string limitKey,
        Func<SubscriptionPlan, long?> planSelector,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
        => ResolveCoreAsync(db, tenant.Id, tenant.PlanId, limitKey, planSelector, nowUtc, cancellationToken);

    private static async Task<EffectivePlanLimit> ResolveCoreAsync(
        AppDbContext db,
        Guid tenantId,
        string? planId,
        string limitKey,
        Func<SubscriptionPlan, long?> planSelector,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // Load the ROW, then apply the selector in memory. Selecting the limit column alone is exactly what
        // collapsed "no row" and "row with NULL" into one indistinguishable null — the bug itself.
        // subscription_plans is a tiny lookup table (a handful of rows), so materialising it is cheaper than
        // the cleverness required to keep the distinction inside SQL.
        var planRow = await db.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == planId, cancellationToken);

        // A deployment with NO plans at all is not misconfigured -- it simply is not using plan-based
        // limiting, so there is no cap to enforce and no revenue rule to protect. The bug is narrower than
        // "the plan did not resolve": it is "plans EXIST and this tenant points at one that does not."
        //
        // Learned the hard way: without this distinction the guard denied 83 tests whose fixtures create a
        // tenant and never seed subscription_plans -- i.e. it was reporting "misconfigured" at deployments
        // that had deliberately configured nothing. Denying those would have been a far broader behaviour
        // change than the fail-open it was fixing.
        var anyPlansConfigured = await db.SubscriptionPlans.AsNoTracking().AnyAsync(cancellationToken);

        var overrides = await db.PlanLimitOverrides
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        long? planValue = planRow is null ? null : planSelector(planRow);

        // Delegated so the PlanExists rule has ONE implementation, shared with the batch-loading callers.
        return ResolveFromLoaded(limitKey, anyPlansConfigured, planRow is not null, planValue, overrides, nowUtc);
    }
}
