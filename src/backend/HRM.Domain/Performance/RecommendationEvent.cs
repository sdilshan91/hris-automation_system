using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Domain.Performance;

/// <summary>
/// An IMMUTABLE, append-only audit entry for a <see cref="Recommendation"/> (US-PRF-010 FR-4/FR-7). Each workflow
/// action (created, overridden, submitted, approved, rejected, integration-raised) writes ONE row capturing the
/// actor, action, actor name, timestamp and optional detail. Rows are NEVER updated or deleted — there is no
/// update path and no concurrency token — so the ordered set of rows IS the compliance/approval audit trail.
/// Directly mirrors the US-PRF-008 <c>PipEvent</c> immutable-log pattern. Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c> (NFR-2). Maps to the
/// "recommendation_event" table.
/// </summary>
public sealed class RecommendationEvent : BaseEntity
{
    /// <summary>The recommendation this event belongs to (FK, required).</summary>
    public Guid RecommendationId { get; set; }

    /// <summary>The action captured (FR-4/FR-7).</summary>
    public RecommendationEventType EventType { get; set; }

    /// <summary>The acting user's id. Null for system actions.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>The acting user's employee id, when linked (traceability). Null for system actions.</summary>
    public Guid? ActorEmployeeId { get; set; }

    /// <summary>The actor's full name as captured at action time. "System" for automated actions.</summary>
    public string ActorName { get; set; } = string.Empty;

    /// <summary>UTC timestamp the action was recorded.</summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>The client IP captured from the request (audit). Null for system actions.</summary>
    public string? ClientIpAddress { get; set; }

    /// <summary>
    /// Optional non-sensitive workflow detail for the event (e.g. the approver step, the rejection comment, the
    /// raised integration target). Plain text, max 4000 chars. NOT used for the sensitive compensation figures.
    /// </summary>
    public string? Detail { get; set; }

    // ── Navigation ─────────────────────────────────────────────────────
    public Recommendation? Recommendation { get; set; }
}
