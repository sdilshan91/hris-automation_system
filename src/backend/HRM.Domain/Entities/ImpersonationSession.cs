namespace HRM.Domain.Entities;

/// <summary>
/// US-ADM-003 (FR-4): a System Admin / System Support impersonation session. This is SYSTEM-LEVEL,
/// CROSS-TENANT platform data — it deliberately does NOT inherit <see cref="BaseEntity"/> and carries
/// NO tenant global query filter, because the impersonator (a system user) and the target (a tenant user)
/// live in different tenant scopes and the session is administered from the system/admin context.
///
/// <para>The row is the source of truth for session-based revocation (AC-3) and the 60-minute expiry cap
/// (NFR-2): the impersonation enforcement middleware loads it by <see cref="Id"/> (carried in the token's
/// <c>imp_session_id</c> claim) on every impersonated request and rejects the request once the status is no
/// longer <see cref="ImpersonationSessionStatus.Active"/> or <see cref="ExpiresAt"/> has passed. There is no
/// access-token denylist; killing the session row is what makes "End Session" and expiry effective.</para>
/// </summary>
public sealed class ImpersonationSession
{
    public Guid Id { get; set; }

    /// <summary>The System Admin / System Support user who initiated the session.</summary>
    public Guid ImpersonatorUserId { get; set; }

    /// <summary>The tenant user being impersonated (the token's <c>sub</c>).</summary>
    public Guid TargetUserId { get; set; }

    /// <summary>The tenant whose workspace is being operated (the token's <c>tenant_id</c>).</summary>
    public Guid TargetTenantId { get; set; }

    /// <summary>The mandatory reason, stored VERBATIM (BR-4). Included in the tenant-admin notification.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Whether the session is read-only (AC-5/AC-6/BR-1): true when the initiator is System Support OR the
    /// target tenant is Suspended. A read-only session blocks every write command (403).
    /// </summary>
    public bool IsReadOnly { get; set; }

    public ImpersonationSessionStatus Status { get; set; } = ImpersonationSessionStatus.Active;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Hard expiry — capped at 60 minutes from start (NFR-2). The token cannot be refreshed.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Set when the session is explicitly ended (AC-3) or lazily flipped to Expired.</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>Best-effort count of mutating actions performed under the session (FR-4, AC-3 summary).</summary>
    public int ActionsCount { get; set; }
}

public enum ImpersonationSessionStatus
{
    Active = 0,
    Ended = 1,
    Expired = 2,
}
