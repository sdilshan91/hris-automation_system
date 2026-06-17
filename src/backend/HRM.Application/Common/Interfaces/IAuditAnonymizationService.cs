namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-NTF-004 (BR-6): GDPR "right to be forgotten" for the audit trail. The <c>audit_log</c> table is
/// append-only and immutable by convention (BR-1) — but a verified erasure request is the ONE sanctioned
/// reason to mutate existing rows. This service does NOT delete the rows (that would destroy the audit
/// trail's integrity / row count); it ANONYMIZES the personal data IN PLACE, replacing identifying field
/// values with a stable <c>REDACTED-{id}</c> token while preserving every row's structure, action,
/// resource and timing. Implemented in Infrastructure.
/// </summary>
public interface IAuditAnonymizationService
{
    /// <summary>
    /// Anonymizes PII in EVERY audit row attributable to <paramref name="userId"/> (as actor OR as the
    /// resource of an identity-bearing action), across all tenants the user belonged to. The actor email,
    /// IP, user-agent, and any value inside the before/after JSON that matches the user's known identifiers
    /// are replaced with <c>REDACTED-{userId}</c>. Returns the number of rows anonymized. Idempotent:
    /// re-running it on already-redacted rows is a no-op.
    /// </summary>
    Task<int> AnonymizeUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
