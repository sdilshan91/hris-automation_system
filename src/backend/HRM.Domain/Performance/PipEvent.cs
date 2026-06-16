using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Domain.Performance;

/// <summary>
/// An IMMUTABLE, append-only audit entry for a <see cref="Pip"/> (US-PRF-008 FR-5/AC-5). Each workflow action
/// (created, initiated, acknowledged / not-acknowledged, checkpoint recorded, extended, outcome set,
/// escalation confirmed) writes ONE row capturing the actor, action, actor name, timestamp and optional notes.
/// Rows are NEVER updated or deleted — there is no update path and no concurrency token — so the ordered set of
/// rows IS the compliance/legal audit trail (FR-5). Directly mirrors the US-PRF-006 <c>ReviewSignoff</c>
/// immutable-log pattern. Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter +
/// <c>TenantInterceptor</c> (NFR-2). Maps to the "pip_event" table.
/// </summary>
public sealed class PipEvent : BaseEntity
{
    /// <summary>The PIP this event belongs to (FK, required).</summary>
    public Guid PipId { get; set; }

    /// <summary>The action captured (FR-5).</summary>
    public PipEventType EventType { get; set; }

    /// <summary>The acting user's id. Null for system actions (e.g. not-acknowledged auto-flag).</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>The acting user's employee id, when linked (traceability). Null for system actions.</summary>
    public Guid? ActorEmployeeId { get; set; }

    /// <summary>The actor's full name as captured at action time (FR-5). "System" for automated actions.</summary>
    public string ActorName { get; set; } = string.Empty;

    /// <summary>UTC timestamp the action was recorded (FR-5).</summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>The client IP captured from the request (FR-5 audit). Null for system actions.</summary>
    public string? ClientIpAddress { get; set; }

    /// <summary>
    /// Optional notes/detail for the event (e.g. the new end date for an extension, the outcome chosen, the
    /// escalation action). Plain text, max 4000 chars. NOT used for the sensitive Reason/EscalationNotes which
    /// live on the PIP (NFR-4 encryption seam) — this column carries non-sensitive workflow detail.
    /// </summary>
    public string? Detail { get; set; }

    // ── Navigation ─────────────────────────────────────────────────────
    public Pip? Pip { get; set; }
}
