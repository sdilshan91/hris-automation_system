namespace HRM.Domain.Entities;

/// <summary>
/// A TENANT-SPECIFIC override of an email notification template for one (<see cref="EventKey"/>,
/// <see cref="Language"/>) pair (US-NTF-002 §7, AC-3). Tenant-scoped via <see cref="BaseEntity"/>: TenantId is
/// auto-stamped by the TenantInterceptor and read-isolated by the AppDbContext global query filter (AC-5/NFR-2).
/// When an active override exists for an event it takes precedence over the platform
/// <see cref="SystemNotificationTemplate"/>; otherwise resolution falls back to the system default (BR-1/FR-6).
///
/// <para>"Reset to default" (AC-4) soft-deletes this row (<see cref="BaseEntity.IsDeleted"/>) so future emails
/// revert to the system default; the change is audited by the existing AuditInterceptor.</para>
/// </summary>
public sealed class NotificationTemplate : BaseEntity
{
    /// <summary>The catalog event key this override applies to, e.g. "leave_approved" (§7).</summary>
    public string EventKey { get; set; } = string.Empty;

    /// <summary>ISO 639-1 language code; default "en" (§7, FR-5).</summary>
    public string Language { get; set; } = "en";

    /// <summary>Customized subject line, supports {{placeholders}} (§7, FR-1).</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Customized HTML body, supports {{placeholders}} (§7, FR-1/BR-3).</summary>
    public string BodyHtml { get; set; } = string.Empty;

    /// <summary>Customized plain-text body, supports {{placeholders}} (§7, FR-1/BR-3).</summary>
    public string BodyText { get; set; } = string.Empty;

    /// <summary>Whether the override is active. Inactive/soft-deleted overrides fall back to the default (§7).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Auto-incremented on every save of the override (§7, FR-9 versioning).</summary>
    public int Version { get; set; } = 1;
}
