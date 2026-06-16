namespace HRM.Domain.Entities;

/// <summary>
/// Audit log entry for security-relevant events (MFA, login, etc.) AND structured business-action audit
/// trails (US-PAY-012). Tenant-scoped for per-tenant audit trails.
///
/// <para>The original shape (Id/TenantId/UserId/EventType/Detail/IpAddress/UserAgent/CreatedAt) is used by
/// AuthService, EmployeeService, ScorecardService, ApplicantService, etc. US-PAY-012 EXTENDS this table
/// additively with the structured columns required by the technical-doc §19.9 audit_log spec — all
/// NULLABLE so existing writers are unaffected. A structured payroll audit entry sets <see cref="Action"/>,
/// <see cref="ResourceType"/>, <see cref="ResourceId"/>, <see cref="Before"/>/<see cref="After"/> (JSON) and
/// the actor/trace columns, while still populating <see cref="EventType"/> (mirrors <see cref="Action"/>) so
/// the legacy reads keep working. There is intentionally no second audit table.</para>
/// </summary>
public sealed class AuditLog
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Structured audit columns (US-PAY-012, technical doc §19.9) — all NULLABLE (additive) ──

    /// <summary>
    /// Standardized action name, e.g. "SalaryComponent.Updated", "PayrollRun.Finalized". Null for legacy
    /// (EventType-only) entries. For structured entries this mirrors <see cref="EventType"/>.
    /// See <see cref="PayrollAuditAction"/> for the payroll action catalog.
    /// </summary>
    public string? Action { get; set; }

    /// <summary>Resource type the action targeted, e.g. "PayrollRun", "SalaryComponent". Nullable.</summary>
    public string? ResourceType { get; set; }

    /// <summary>Identifier of the targeted resource (usually a GUID string). Nullable.</summary>
    public string? ResourceId { get; set; }

    /// <summary>Old values as JSON (null for create actions). Stored as jsonb on PostgreSQL.</summary>
    public string? Before { get; set; }

    /// <summary>New values as JSON (null for delete actions). Stored as jsonb on PostgreSQL.</summary>
    public string? After { get; set; }

    /// <summary>The actor's employee number, when known (FR-3). Nullable.</summary>
    public string? ActorEmployeeNo { get; set; }

    /// <summary>Distributed trace identifier for correlation (FR-3, technical doc §19.9). Nullable.</summary>
    public string? TraceId { get; set; }

    // ── Impersonator attribution (US-ADM-003 FR-3/AC-2) — all NULLABLE (additive) ──

    /// <summary>
    /// When this row was written during an impersonation session, the System Admin / System Support user who
    /// was operating the workspace (NOT the impersonated <see cref="UserId"/>). Null for ordinary actions.
    /// Stamped automatically by <c>AuditInterceptor</c> from <c>ICurrentUser</c> when impersonating.
    /// </summary>
    public Guid? ImpersonatorUserId { get; set; }

    /// <summary>The <c>impersonation_sessions</c> row id this action belongs to (US-ADM-003 AC-2). Nullable.</summary>
    public Guid? ImpersonationSessionId { get; set; }

    /// <summary>True iff this audit row was produced while an impersonation session was active. Default false.</summary>
    public bool IsImpersonationAction { get; set; }
}
