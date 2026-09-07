namespace HRM.Application.Common.Helpers;

/// <summary>
/// ISSUE-062 (US-AUTH-010 FR-7): the auth security events that must be visible to the PLATFORM operator,
/// not just to the tenant they happened in.
///
/// <para>The platform copy is a SECOND <c>audit_logs</c> row carrying <c>TenantId = null</c> — the existing
/// system-scoped convention (<see cref="HRM.Domain.Entities.AuditLog"/> documents that "there is
/// intentionally no second audit table", and <c>audit_logs</c> is the one entity whose global query filter
/// carries a <c>TenantId == null</c> arm precisely so those rows survive). <c>PlatformMonitoringService</c>
/// writes its access rows the same way. FR-7 wants BOTH views, so the duplication is deliberate: the tenant
/// keeps its own audit trail intact and the platform gets a cross-tenant one.</para>
///
/// <para>This list lives here — in ONE place — rather than as a flag threaded through every call site, so a
/// new lockout/unlock writer cannot forget to opt in. When the platform-facing read surface
/// (<c>api/v1/system/audit-logs</c>, parked pending a permissions/masking decision) is built, it reads the
/// same list.</para>
/// </summary>
public static class PlatformVisibleAuditEvents
{
    /// <summary>Account was locked after reaching the failed-attempt threshold (AC-2).</summary>
    public const string AccountLocked = "account_locked";

    /// <summary>Lockout expired on its own and the account was reopened (AC-4).</summary>
    public const string AccountUnlockedByTimeout = "account_unlocked_by_timeout";

    /// <summary>A tenant administrator unlocked the account early (AC-5).</summary>
    public const string AccountUnlockedByAdmin = "account_unlocked_by_admin";

    private static readonly HashSet<string> Events = new(StringComparer.Ordinal)
    {
        AccountLocked,
        AccountUnlockedByTimeout,
        AccountUnlockedByAdmin,
    };

    /// <summary>
    /// True when <paramref name="eventType"/> must also be recorded as a system-scoped
    /// (<c>TenantId = null</c>) audit row alongside the tenant-scoped one.
    /// </summary>
    public static bool Requires(string? eventType) => eventType is not null && Events.Contains(eventType);
}
