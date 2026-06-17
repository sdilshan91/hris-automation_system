using HRM.Domain.Entities;

namespace HRM.Domain.Authorization;

/// <summary>
/// The single, side-effect-free source of truth for resolving an effective plan limit for a tenant
/// (US-ADM-009 FR-4). Resolution order:
/// <list type="number">
///   <item>A non-expired <see cref="PlanLimitOverride"/> for the tenant+key WINS over the plan field.</item>
///   <item>Otherwise the plan's own field value is used.</item>
/// </list>
/// In both cases a <c>null</c> value means "unlimited" (US-ADM-009 BR-3) — represented by a null result.
/// Expired overrides (<see cref="PlanLimitOverride.ExpiresAt"/> &lt;= <c>nowUtc</c>) are ignored.
/// </summary>
public static class PlanLimitResolver
{
    /// <summary>
    /// The resolved limit. <see cref="Value"/> is the effective cap; <c>null</c> means UNLIMITED. <see cref="Source"/>
    /// indicates whether the value came from an override or the plan, for diagnostics/auditing.
    /// </summary>
    public readonly record struct ResolvedLimit(long? Value, LimitSource Source)
    {
        /// <summary>True when there is no cap (unlimited).</summary>
        public bool IsUnlimited => Value is null;
    }

    public enum LimitSource
    {
        /// <summary>The value came from the plan field (no applicable override).</summary>
        Plan,

        /// <summary>The value came from a non-expired per-tenant override.</summary>
        Override,
    }

    /// <summary>
    /// Resolves the effective limit for a single key.
    /// </summary>
    /// <param name="limitKey">The canonical limit key (see <see cref="PlanLimitKeys"/>).</param>
    /// <param name="planValue">The plan's value for this key (null = unlimited per the plan).</param>
    /// <param name="overrides">All known overrides for the tenant (any keys/expiries — filtered here).</param>
    /// <param name="nowUtc">The current UTC time, used to evaluate expiry.</param>
    public static ResolvedLimit Resolve(
        string limitKey,
        long? planValue,
        IEnumerable<PlanLimitOverride>? overrides,
        DateTime nowUtc)
    {
        if (overrides is not null)
        {
            // Pick the applicable, non-expired override for this key. If several exist, the most recently
            // created one wins (deterministic) — the management API enforces one-per-(tenant,key) anyway.
            PlanLimitOverride? applicable = null;
            foreach (var o in overrides)
            {
                if (!string.Equals(o.LimitKey, limitKey, StringComparison.Ordinal))
                    continue;
                if (o.ExpiresAt is { } exp && exp <= nowUtc)
                    continue; // expired — ignore (FR-4)
                if (applicable is null || o.CreatedAt > applicable.CreatedAt)
                    applicable = o;
            }

            if (applicable is not null)
                return new ResolvedLimit(applicable.Value, LimitSource.Override); // null Value = unlimited override
        }

        return new ResolvedLimit(planValue, LimitSource.Plan);
    }
}
