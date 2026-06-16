namespace HRM.Domain.Entities;

/// <summary>
/// An append-only record of a tenant lifecycle transition (US-ADM-001 AC-4), e.g. "created", "suspended",
/// "terminated". This is a SYSTEM/cross-tenant table: it carries a <see cref="TenantId"/> but is NOT
/// tenant-scoped via the global query filter, because the System Admin operates ACROSS tenants and must be
/// able to read lifecycle history for any tenant (and write rows while no tenant is resolved in the admin
/// context). It does NOT inherit <see cref="BaseEntity"/>.
/// </summary>
public sealed class TenantLifecycleEvent
{
    public Guid Id { get; set; }

    /// <summary>The tenant this event belongs to.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Lifecycle event type, e.g. "created". Stored as a lowercase string.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Optional JSON payload with event details (e.g. plan, owner email). Stored as jsonb.</summary>
    public string? DetailJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Identifier (user id / email) of the actor who triggered the event.</summary>
    public string? CreatedBy { get; set; }
}
