namespace HRM.Domain.Entities;

/// <summary>
/// A Hangfire job scheduled as part of a tenant's termination grace period (US-ADM-004 FR-2/FR-6): the
/// data-deletion job and the 14d/7d/1d reminder-notification jobs. Recorded so Restore (AC-6/FR-6) can
/// de-queue every outstanding job by its Hangfire id.
///
/// <para>This is a SYSTEM/cross-tenant table (like <see cref="TenantLifecycleEvent"/>): it carries a
/// <see cref="TenantId"/> but is NOT tenant-scoped via the global query filter, because the System Admin
/// schedules/cancels these while operating in the admin context (no tenant resolved). It does NOT inherit
/// <see cref="BaseEntity"/>.</para>
/// </summary>
public sealed class TenantScheduledJob
{
    public Guid Id { get; set; }

    /// <summary>The tenant this scheduled job belongs to.</summary>
    public Guid TenantId { get; set; }

    /// <summary>The Hangfire background-job id, used to <c>Delete</c> the job on Restore.</summary>
    public string JobId { get; set; } = string.Empty;

    /// <summary>What the job does: "deletion" or "reminder" (lowercase). Informational / for filtering.</summary>
    public string JobType { get; set; } = string.Empty;

    /// <summary>When the job is scheduled to fire (UTC). Informational.</summary>
    public DateTime ScheduledFor { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
