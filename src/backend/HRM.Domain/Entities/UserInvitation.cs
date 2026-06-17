namespace HRM.Domain.Entities;

/// <summary>
/// US-ADM-005: a pending invitation for a user to join a tenant workspace (AC-2 / FR-2).
/// Tenant-scoped — inherits <see cref="BaseEntity"/> so the EF global query filter and the
/// TenantInterceptor handle isolation + tenant stamping automatically.
///
/// <para>The pending membership IS this row: a brand-new user is NOT given a UserTenant until the
/// invitation is accepted (acceptance flow is owned by AUTH/onboarding, deferred). The "invited"
/// state surfaced to the UI (AC-1) is derived from an <see cref="InvitationStatus.Invited"/> row, since
/// <c>UserTenantStatus</c> has no "Invited" value. Only the token HASH is stored — the raw one-time
/// token is embedded in the (log-only) invitation email and never persisted (mirrors RefreshToken).</para>
/// </summary>
public sealed class UserInvitation : BaseEntity
{
    public string Email { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the one-time invitation token. The raw token is never stored (FR-2).</summary>
    public string TokenHash { get; set; } = string.Empty;

    public InvitationStatus Status { get; set; } = InvitationStatus.Invited;

    /// <summary>When the one-time token expires (72h from issue, BR-6).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>The Tenant Admin (User.Id) who issued the invitation.</summary>
    public Guid InvitedByUserId { get; set; }

    /// <summary>The role ids to assign on acceptance. Stored as a uuid[] column.</summary>
    public List<Guid> InvitedRoleIds { get; set; } = new();

    /// <summary>When the invitation was accepted (null until accepted).</summary>
    public DateTime? AcceptedAt { get; set; }

    /// <summary>
    /// True iff this invitation is still actionable: Invited and not past expiry. Expired-by-time
    /// invitations report not-pending even if their Status column still reads Invited (BR-6).
    /// </summary>
    public bool IsPending => Status == InvitationStatus.Invited && ExpiresAt > DateTime.UtcNow;
}

/// <summary>Lifecycle of a <see cref="UserInvitation"/>.</summary>
public enum InvitationStatus
{
    Invited = 0,
    Accepted = 1,
    Expired = 2,
    Revoked = 3
}
