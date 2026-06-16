namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Writes a structured payroll audit-log entry (US-PAY-012 FR-2/FR-3) into the shared <c>audit_log</c> table.
/// A thin helper over the existing AuditLog entity: it stamps the standardized action, resource_type/id,
/// before/after JSON, the actor (from <see cref="ICurrentUser"/>, or the system actor for job-driven actions
/// per BR-7), and — for sensitive actions — the request IP / user-agent (BR-5).
///
/// <para>The entry is ADDED to the current <see cref="HRM.Infrastructure.Persistence.AppDbContext"/> change
/// tracker. By default it is NOT persisted here — the caller's own <c>SaveChanges</c> commits it atomically
/// with the business change (so an audit row never exists for a write that rolled back). A SaveChanges
/// variant is provided for callers that have already committed (e.g. a separate job step).</para>
///
/// <para>NFR-1 (async/outbox) is a documented follow-up — writes are synchronous for now.</para>
/// </summary>
public interface IPayrollAuditLogger
{
    /// <summary>
    /// Stages a structured audit entry on the current DbContext (not saved — the caller's SaveChanges commits
    /// it). <paramref name="before"/>/<paramref name="after"/> are serialized to JSON; pass the domain object
    /// (or an anonymous projection of the changed fields) or null. When <paramref name="systemActor"/> is true
    /// the actor is the well-known system actor id (BR-7) instead of the current user.
    /// </summary>
    void Log(
        string action,
        string resourceType,
        string resourceId,
        object? before = null,
        object? after = null,
        string? ipAddress = null,
        string? userAgent = null,
        bool systemActor = false);

    /// <summary>
    /// Stages a structured audit entry and immediately persists ONLY the audit entry (its own SaveChanges).
    /// Use from a job step that has already committed its business change and wants a standalone audit row.
    /// </summary>
    Task LogAndSaveAsync(
        string action,
        string resourceType,
        string resourceId,
        object? before = null,
        object? after = null,
        string? ipAddress = null,
        string? userAgent = null,
        bool systemActor = false,
        CancellationToken cancellationToken = default);
}
