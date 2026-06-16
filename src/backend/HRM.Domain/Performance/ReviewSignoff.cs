using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Domain.Performance;

/// <summary>
/// An IMMUTABLE, append-only digital sign-off record for a <see cref="ManagerReview"/> (US-PRF-006
/// FR-3/FR-4/FR-7, NFR-3/BR-5). Each workflow action (manager requests sign-off, employee acknowledges or
/// disputes, HR resolves, system auto-closes) writes ONE row capturing the party, action, signer name,
/// timestamp and client IP — and optional dispute/resolution comments. Rows are NEVER updated or deleted;
/// the service has no update path, and there is no concurrency token because rows are write-once. The
/// ordered set of rows IS the audit trail of the sign-off (FR-7). Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c> (NFR-2). Maps to
/// the "review_signoff" table.
/// </summary>
public sealed class ReviewSignoff : BaseEntity
{
    /// <summary>The manager review this sign-off entry belongs to (FK, required).</summary>
    public Guid ManagerReviewId { get; set; }

    /// <summary>Which party recorded this entry — Manager / Employee / Hr (FR-3).</summary>
    public SignoffParty Party { get; set; }

    /// <summary>The action captured — RequestedSignOff / Acknowledged / Disputed / DisputeConfirmed / DisputeAmended / AutoClosedNoResponse.</summary>
    public SignoffAction Action { get; set; }

    /// <summary>The signer's full name as captured at sign time (FR-3, "name + timestamp"). Empty for system auto-close.</summary>
    public string SignerName { get; set; } = string.Empty;

    /// <summary>The acting user's id (FR-7). Null for system auto-close.</summary>
    public Guid? SignerUserId { get; set; }

    /// <summary>The acting user's employee id, when linked (traceability). Null for system actions.</summary>
    public Guid? SignerEmployeeId { get; set; }

    /// <summary>UTC timestamp the action was recorded (FR-3/FR-7).</summary>
    public DateTime SignedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The client IP captured from the request (FR-7). Null for system auto-close.</summary>
    public string? ClientIpAddress { get; set; }

    /// <summary>
    /// Mandatory dispute comments when <see cref="Action"/> is <see cref="SignoffAction.Disputed"/> (FR-4/FR-5),
    /// or HR's resolution comments for DisputeConfirmed / DisputeAmended (BR-4). Plain text, max 4000 chars.
    /// </summary>
    public string? Comments { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public ManagerReview? ManagerReview { get; set; }
}
