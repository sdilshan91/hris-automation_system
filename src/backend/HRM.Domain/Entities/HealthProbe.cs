namespace HRM.Domain.Entities;

/// <summary>
/// SYSTEM-scope record of one platform readiness probe (US-ADM-002 FR-7 / TC-ADM-002-17). The SLA uptime
/// percentage is derived from these rows; without a retained probe history there is nothing to compute it from,
/// which is why the dashboard reported <c>SlaUptimePercent: null</c>.
///
/// <para>Deliberately NOT a <see cref="BaseEntity"/>: like <c>encryption_key_activation</c> and
/// <c>data_protection_keys</c> this is a platform-wide table with NO <c>TenantId</c> — the API instance is
/// shared, so uptime is a property of the PLATFORM, not of a tenant. What is per-tenant is only the
/// COMPARISON: the same measured uptime is judged against each tenant's plan <c>SlaTier</c> threshold. The
/// tenant interceptor / query-filter / RLS-policy rules for tenant tables therefore do not apply.</para>
///
/// <para>Append-only and pruned on a retention window by the recording job — an unbounded probe table would
/// grow without limit at one row per probe interval, forever.</para>
/// </summary>
public sealed class HealthProbe
{
    public Guid Id { get; set; }

    /// <summary>UTC instant the probe ran.</summary>
    public DateTime ObservedAtUtc { get; set; }

    /// <summary>
    /// True when the readiness check reported Healthy. Degraded counts as NOT healthy for SLA purposes: a
    /// degraded platform is not meeting its availability promise, and silently counting it as up is exactly the
    /// kind of flattering measurement TC-ADM-002-17 forbids.
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>The raw health-check status name (Healthy / Degraded / Unhealthy), kept for diagnosis.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>How long the readiness check took, milliseconds.</summary>
    public int DurationMs { get; set; }
}
