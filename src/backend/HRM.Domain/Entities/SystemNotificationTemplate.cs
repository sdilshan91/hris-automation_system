namespace HRM.Domain.Entities;

/// <summary>
/// A PLATFORM-LEVEL (system-default) email notification template for one (<see cref="EventKey"/>,
/// <see cref="Language"/>) pair (US-NTF-002 §7, BR-1/BR-2). Seeded during platform deployment and managed by
/// System Admins; read-only for tenants.
///
/// <para><b>Why a separate entity from <see cref="NotificationTemplate"/>:</b> the story models a "system
/// default" as a template row with a NULL <c>tenant_id</c>. But <see cref="BaseEntity"/> auto-stamps
/// <c>TenantId</c> via the TenantInterceptor and the AppDbContext global query filter forces
/// <c>TenantId == current</c> — which would HIDE every NULL-tenant default from a resolved tenant. Modelling the
/// system defaults as their own NON-tenant-scoped table (no global query filter, like
/// <c>subscription_plans</c>) keeps the tenant filter on the OVERRIDE table (<see cref="NotificationTemplate"/>)
/// fully intact while letting resolution fall back to the shared defaults. This is NOT a <see cref="BaseEntity"/>
/// so it carries no <c>TenantId</c> at all.</para>
/// </summary>
public sealed class SystemNotificationTemplate
{
    /// <summary>Surrogate PK (UUIDv7).</summary>
    public Guid Id { get; set; }

    /// <summary>The catalog event key this template renders, e.g. "leave_approved" (§7).</summary>
    public string EventKey { get; set; } = string.Empty;

    /// <summary>ISO 639-1 language code; default "en" (§7, FR-5).</summary>
    public string Language { get; set; } = "en";

    /// <summary>Subject line, supports {{placeholders}} (§7).</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>HTML body, supports {{placeholders}} (§7, BR-3).</summary>
    public string BodyHtml { get; set; } = string.Empty;

    /// <summary>Plain-text body, supports {{placeholders}} (§7, BR-3 — required for email-client compatibility).</summary>
    public string BodyText { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
